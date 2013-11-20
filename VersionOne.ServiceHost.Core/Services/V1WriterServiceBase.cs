using System;
using System.Collections.Generic;
using System.Xml;
using VersionOne.SDK.APIClient;
using VersionOne.ServiceHost.Eventing;
using VersionOne.Profile;
using VersionOne.ServiceHost.Core.Logging;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VersionOne.ServiceHost.Core.Services 
{

    public class V1Connection
    {
        public readonly IMetaModel Meta;
        public readonly IServices Data;
        public readonly ILocalizer Localization;
        
        public V1Connection(IMetaModel meta, IServices data, ILocalizer local)
        {
            Meta = meta;
            Data = data;
            Localization = local;
        }
    }

    public abstract class V1WriterServiceBase : IHostedService 
    {
        private V1Connection _v1Connection;
        protected XmlElement Config;
        protected IEventManager EventManager;
        protected ILogger Logger;

        private const string MemberType = "Member";
        private const string DefaultRoleNameProperty = "DefaultRole.Name";

        //Note that when using OAuth, the client_secrets.json and stored_credentials.json files must exist in the application folder.
        protected virtual V1Connection V1Connection
        {
            get
            {
                if (_v1Connection == null)
                {
                    try
                    {
                        IMetaModel metaService;
                        IServices dataService;
                        ILocalizer localService;

                        //Use OAuth.
                        if (File.Exists("client_secrets.json") == true)
                        {
                            if (File.Exists("stored_credentials.json") == false)
                                throw new Exception("The stored_credentials.json file was not found.");

                            string baseUrl = GetBaseURLFromJSONFile("client_secrets.json");

                            V1OAuth2APIConnector metaConnector = new V1OAuth2APIConnector(baseUrl + "/meta.v1/");
                            V1OAuth2APIConnector dataConnector = new V1OAuth2APIConnector(baseUrl + "/rest-1.oauth.v1/");
                            V1APIConnector localConnector = new V1APIConnector(baseUrl + "/loc.v1/");

                            metaService = new VersionOne.SDK.APIClient.MetaModel(metaConnector);
                            dataService = new VersionOne.SDK.APIClient.Services(metaService, dataConnector);
                            localService = new VersionOne.SDK.APIClient.Localizer(localConnector);
                        }

                        //Use Basic|Windows.
                        else
                        {
                            string baseUrl = Config["Settings"].SelectSingleNode("ApplicationUrl").InnerText;
                            string username = Config["Settings"].SelectSingleNode("Username").InnerText;
                            string password = Config["Settings"].SelectSingleNode("Password").InnerText;
                            bool authType = Config["Settings"].SelectSingleNode("IntegratedAuth").InnerText == "true" ? true : false;

                            V1APIConnector metaConnector = new V1APIConnector(baseUrl + "meta.v1/");
                            V1APIConnector dataConnector = new V1APIConnector(baseUrl + "rest-1.v1/", username, password, authType);
                            V1APIConnector localConnector = new V1APIConnector(baseUrl + "loc.v1/");

                            metaService = new VersionOne.SDK.APIClient.MetaModel(metaConnector);
                            dataService = new VersionOne.SDK.APIClient.Services(metaService, dataConnector);
                            localService = new VersionOne.SDK.APIClient.Localizer(localConnector);
                        }

                        _v1Connection = new V1Connection(metaService, dataService, localService);
                        LogVersionOneConnectionInformation(V1Connection.Meta, V1Connection.Data);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to connect to VersionOne instance.", ex);
                        throw;
                    }
                }
                return _v1Connection;
            }
        }

        private string GetBaseURLFromJSONFile(string JSONFileName)
        {
            using (StreamReader reader = File.OpenText(JSONFileName))
            {
                JObject json = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                return (string)json["installed"]["server_base_uri"];
            }
        }

        private void LogVersionOneConnectionInformation(IMetaModel meta, IServices service) 
        {
            try 
            {
                string metaVersion = ((MetaModel)meta).Version.ToString();
                string memberOid = service.LoggedIn.Momentless.ToString();
                string defaultRole = GetLoggedInMemberRole(meta, service);

                Logger.LogVersionOneConnectionInformation(LogMessage.SeverityType.Info, metaVersion, memberOid, defaultRole);
            } 
            catch (Exception ex) 
            {
                Logger.Log(LogMessage.SeverityType.Warning, "Failed to log VersionOne connection information.", ex);
            }
        }

        private string GetLoggedInMemberRole(IMetaModel meta, IServices service) 
        {
            var query = new Query(service.LoggedIn);
            var defaultRoleAttribute = meta.GetAssetType(MemberType).GetAttributeDefinition(DefaultRoleNameProperty);
            query.Selection.Add(defaultRoleAttribute);

            var asset = service.Retrieve(query).Assets[0];
            var role = asset.GetAttribute(defaultRoleAttribute);
            return V1Connection.Localization.Resolve(role.Value.ToString());
        }

        public virtual void Initialize(XmlElement config, IEventManager eventManager, IProfile profile) 
        {
            Config = config;
            EventManager = eventManager;
            Logger = new Logger(eventManager);
            Logger.LogVersionOneConfiguration(LogMessage.SeverityType.Info, Config["Settings"]);
        }

        public void Start() 
        {
            // TODO: Move subscriptions to timer events, etc. here
        }

        protected abstract IEnumerable<NeededAssetType> NeededAssetTypes { get; }

        protected void VerifyMeta() 
        {
            try 
            {
                VerifyNeededMeta(NeededAssetTypes);
                VerifyRuntimeMeta();
            } 
            catch (MetaException ex) 
            {
                throw new ApplicationException("Necessary meta is not present in this VersionOne instance.", ex);
            }
        }

        protected virtual void VerifyRuntimeMeta() { }

        protected struct NeededAssetType 
        {
            public readonly string Name;
            public readonly string[] AttributeDefinitionNames;

            public NeededAssetType(string name, string[] attributedefinitionnames) 
            {
                Name = name;
                AttributeDefinitionNames = attributedefinitionnames;
            }
        }

        protected void VerifyNeededMeta(IEnumerable<NeededAssetType> neededassettypes) 
        {
            foreach(var neededAssetType in neededassettypes) 
            {
                IAssetType assettype;
                assettype = V1Connection.Meta.GetAssetType(neededAssetType.Name);

                foreach(var attributeDefinitionName in neededAssetType.AttributeDefinitionNames) 
                {
                    var attribdef = assettype.GetAttributeDefinition(attributeDefinitionName);
                }
            }
        }

        #region Meta wrappers

        protected IAssetType RequestType { get { return V1Connection.Meta.GetAssetType("Request"); } }
        protected IAssetType DefectType { get { return V1Connection.Meta.GetAssetType("Defect"); } }
        protected IAssetType StoryType { get { return V1Connection.Meta.GetAssetType("Story"); } }
        protected IAssetType ReleaseVersionType { get { return V1Connection.Meta.GetAssetType("StoryCategory"); } }
        protected IAssetType LinkType { get { return V1Connection.Meta.GetAssetType("Link"); } }
        protected IAssetType NoteType { get { return V1Connection.Meta.GetAssetType("Note"); } }
        protected IOperation RequestInactivate { get { return V1Connection.Meta.GetOperation("Request.Inactivate"); } }

        protected IAttributeDefinition DefectName { get { return DefectType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition DefectDescription { get { return DefectType.GetAttributeDefinition("Description"); } }
        protected IAttributeDefinition DefectOwners { get { return DefectType.GetAttributeDefinition("Owners"); } }
        protected IAttributeDefinition DefectScope { get { return DefectType.GetAttributeDefinition("Scope"); } }
        protected IAttributeDefinition DefectAssetState { get { return RequestType.GetAttributeDefinition("AssetState"); } }
        protected IAttributeDefinition RequestCompanyName { get { return RequestType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition RequestNumber { get { return RequestType.GetAttributeDefinition("Number"); } }
        protected IAttributeDefinition RequestSuggestedInstance { get { return RequestType.GetAttributeDefinition("Reference"); } }
        protected IAttributeDefinition RequestMethodology { get { return RequestType.GetAttributeDefinition("Source"); } }
        protected IAttributeDefinition RequestMethodologyName { get { return RequestType.GetAttributeDefinition("Source.Name"); } }
        protected IAttributeDefinition RequestCommunityEdition { get { return RequestType.GetAttributeDefinition("Custom_CommunityEdition"); } }
        protected IAttributeDefinition RequestAssetState { get { return RequestType.GetAttributeDefinition("AssetState"); } }
        protected IAttributeDefinition RequestCreateDate { get { return RequestType.GetAttributeDefinition("CreateDate"); } }
        protected IAttributeDefinition RequestCreatedBy { get { return RequestType.GetAttributeDefinition("CreatedBy"); } }
        protected IAttributeDefinition StoryName { get { return StoryType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition StoryActualInstance { get { return StoryType.GetAttributeDefinition("Reference"); } }
        protected IAttributeDefinition StoryRequests { get { return StoryType.GetAttributeDefinition("Requests"); } }
        protected IAttributeDefinition StoryReleaseVersion { get { return StoryType.GetAttributeDefinition("Category"); } }
        protected IAttributeDefinition StoryMethodology { get { return StoryType.GetAttributeDefinition("Source"); } }
        protected IAttributeDefinition StoryCommunitySite { get { return StoryType.GetAttributeDefinition("Custom_CommunitySite"); } }
        protected IAttributeDefinition StoryScope { get { return StoryType.GetAttributeDefinition("Scope"); } }
        protected IAttributeDefinition StoryOwners { get { return StoryType.GetAttributeDefinition("Owners"); } }
        protected IAttributeDefinition ReleaseVersionName { get { return ReleaseVersionType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition LinkAsset { get { return LinkType.GetAttributeDefinition("Asset"); } }
        protected IAttributeDefinition LinkOnMenu { get { return LinkType.GetAttributeDefinition("OnMenu"); } }
        protected IAttributeDefinition LinkUrl { get { return LinkType.GetAttributeDefinition("URL"); } }
        protected IAttributeDefinition LinkName { get { return LinkType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition NoteName { get { return NoteType.GetAttributeDefinition("Name"); } }
        protected IAttributeDefinition NoteAsset { get { return NoteType.GetAttributeDefinition("Asset"); } }
        protected IAttributeDefinition NotePersonal { get { return NoteType.GetAttributeDefinition("Personal"); } }
        protected IAttributeDefinition NoteContent { get { return NoteType.GetAttributeDefinition("Content"); } }

        #endregion
    }
}