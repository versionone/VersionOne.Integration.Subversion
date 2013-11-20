using System;
using System.Collections.Generic;
using System.Xml;
using VersionOne.Profile;
using VersionOne.SDK.APIClient;
using VersionOne.ServiceHost.Core.Services;
using VersionOne.ServiceHost.Core.Utility;
using VersionOne.ServiceHost.Eventing;
using Attribute = VersionOne.SDK.APIClient.Attribute;

namespace VersionOne.ServiceHost.SubversionServices
{
    public class LinkInfo 
    {
        public readonly string Name;
        public readonly bool OnMenu;
        public readonly string Url;

        public LinkInfo(string name, string url, bool onmenu) 
        {
            Name = name;
            Url = url;
            OnMenu = onmenu;
        }
    }

    public class ChangeSetWriterService : V1WriterServiceBase 
    {
        private const string ChangeCommentField = "ChangeComment";
        private const string ReferenceAttributeField = "ReferenceAttribute";
        private const string AlwaysCreateField = "AlwaysCreate";
        private const string RepositoryIdField = "RepositoryIdField";
        private const string FriendlyRepositoryNameField = "RepositoryFriendlyNameField";
        private const string CustomFieldPrefix = "Custom_";

        private bool alwaysCreate;
        private string changecomment;
        private string customRepositoryFriendlyNameField;
        private string customRepositoryIdField;
        private string referencename;

        public override void Initialize(XmlElement config, IEventManager eventManager, IProfile profile) 
        {
            base.Initialize(config, eventManager, profile);
            changecomment = config[ChangeCommentField].InnerText;
            referencename = config[ReferenceAttributeField].InnerText;

            bool alwaysCreateValue = false;
            if(config[AlwaysCreateField] != null) 
            {
                bool.TryParse(config[AlwaysCreateField].InnerText, out alwaysCreateValue);
            }
            alwaysCreate = alwaysCreateValue;

            XmlElement repositoryIdElement = config[RepositoryIdField];
            if(repositoryIdElement == null || string.IsNullOrEmpty(repositoryIdElement.InnerText)) 
            {
                throw new ConfigurationException("Mandatory configuration property RepositoryIdField is not provided.");
            }

            XmlElement repositoryNameElement = config[FriendlyRepositoryNameField];
            if(repositoryNameElement == null || string.IsNullOrEmpty(repositoryNameElement.InnerText)) 
            {
                throw new ConfigurationException("Mandatory configuration property RepositoryFriendlyNameField is not provided.");
            }

            customRepositoryIdField = string.Format("{0}{1}", CustomFieldPrefix, repositoryIdElement.InnerText);
            customRepositoryFriendlyNameField = string.Format("{0}{1}", CustomFieldPrefix, repositoryNameElement.InnerText);

            VerifyMeta();
            eventManager.Subscribe(typeof(ChangeSetInfo), ChangeSetListener);
        }

        protected override void VerifyRuntimeMeta() 
        {
            base.VerifyRuntimeMeta();
            var refdef = PrimaryWorkitemReferenceDef;
        }

        private void ChangeSetListener(object pubobj) 
        {
            var info = (ChangeSetInfo)pubobj;

            try 
            {
                ProcessChangeSetInfo(info);
            } 
            catch(Exception ex) 
            {
                Logger.Log(string.Format("Process Change Set {0} Info Failed: {1}", info.Revision, ex));
            }
        }

        private void ProcessChangeSetInfo(ChangeSetInfo info) 
        {
            IList<Oid> affectedworkitems = GetAffectedWorkitems(info.References);

            Asset changeSet = GetChangeSet(info, affectedworkitems);
            if(changeSet == null) 
            {
                return;
            }

            Asset savedAsset = SaveChangeSetAsset(changeSet, info, affectedworkitems);

            if(info.Link != null) 
            {
                SaveChangeSetLink(info, savedAsset);
            }
        }

        private Asset GetChangeSet(ChangeSetInfo info, IList<Oid> affectedworkitems) 
        {
            Asset changeSet = null;
            AssetList list = FindExistingChangeset(info.Revision, info.RepositoryId).Assets;
            if(list.Count > 0) 
            {
                changeSet = list[0];
                Logger.Log(string.Format("Using existing Change Set: {0} ({1})", info.Revision, changeSet.Oid));
            } 
            else 
            {
                if(ShouldCreate(affectedworkitems)) 
                {
                    changeSet = V1Connection.Data.New(ChangeSetType, Oid.Null);
                    changeSet.SetAttributeValue(ChangeSetReferenceDef, info.Revision);
                    changeSet.SetAttributeValue(ChangeSetRepositoryIdDef, info.RepositoryId);
                } 
                else 
                {
                    Logger.Log("No Change Set References. Ignoring Change Set: " + info.Revision);
                }
            }
            return changeSet;
        }

        private bool ShouldCreate(IList<Oid> affectedworkitems) 
        {
            return (alwaysCreate || (affectedworkitems.Count > 0));
        }

        private QueryResult FindExistingChangeset(int revision, string repositoryId) 
        {
            var q = new Query(ChangeSetType);
            q.Selection.Add(ChangeSetType.GetAttributeDefinition("Reference"));
            q.Selection.Add(ChangeSetType.GetAttributeDefinition("Links.URL"));

            var referenceTerm = new FilterTerm(ChangeSetType.GetAttributeDefinition("Reference"));
            referenceTerm.Equal(revision);

            IFilterTerm term = referenceTerm;

            if(!string.IsNullOrEmpty(customRepositoryIdField) && !string.IsNullOrEmpty(repositoryId)) 
            {
                var uuidTerm = new FilterTerm(ChangeSetType.GetAttributeDefinition(customRepositoryIdField));
                uuidTerm.Equal(repositoryId);
                term = new AndFilterTerm(referenceTerm, uuidTerm);
            }

            q.Filter = term;
            q.Paging = new Paging(0, 1);

            return V1Connection.Data.Retrieve(q);
        }

        private IList<Oid> FindWorkitemOid(string reference) 
        {
            var oids = new List<Oid>();
            var q = new Query(PrimaryWorkitemType);
            var term = new FilterTerm(PrimaryWorkitemReferenceDef);
            term.Equal(reference);
            q.Filter = term;

            AssetList list;
            list = V1Connection.Data.Retrieve(q).Assets;

            foreach(var asset in list) 
            {
                if(!oids.Contains(asset.Oid))
                {
                    oids.Add(asset.Oid);
                }
            }
            return oids;
        }

        private IList<Oid> GetAffectedWorkitems(IEnumerable<string> references) 
        {
            var primaryworkitems = new List<Oid>();

            foreach(var reference in references) 
            {
                var workitemoids = FindWorkitemOid(reference);

                if(workitemoids == null || workitemoids.Count == 0)
                {
                    Logger.Log(string.Format("No {0} or {1} related to reference: {2}", StoryName, DefectName, reference));
                    continue;
                }

                primaryworkitems.AddRange(workitemoids);
            }
            return primaryworkitems;
        }

        private static string GetFormattedTime(DateTime dateTime) 
        {
            var localTime = TimeZone.CurrentTimeZone.ToLocalTime(dateTime);
            var offset = TimeZone.CurrentTimeZone.GetUtcOffset(localTime);
            var result = string.Format("{0} UTC{1}{2}:{3:00}", localTime, offset.TotalMinutes >= 0 ? "+" : string.Empty, offset.Hours, offset.Minutes);
            return result;
        }

        private Asset SaveChangeSetAsset(Asset changeSet, ChangeSetInfo info, IEnumerable<Oid> primaryworkitems) 
        {
            changeSet.SetAttributeValue(ChangeSetNameDef, string.Format("'{0}' on '{1}'", info.Author, GetFormattedTime(info.ChangeDate)));
            changeSet.SetAttributeValue(ChangeSetDescriptionDef, info.Message);

            if(!string.IsNullOrEmpty(info.RepositoryId) && !string.IsNullOrEmpty(customRepositoryIdField)) 
            {
                changeSet.SetAttributeValue(ChangeSetRepositoryIdDef, info.RepositoryId);
            }

            if(!string.IsNullOrEmpty(info.RepositoryFriendlyName) && !string.IsNullOrEmpty(customRepositoryFriendlyNameField)) 
            {
                changeSet.SetAttributeValue(ChangeSetRepositoryFriendlyNameDef, info.RepositoryFriendlyName);
            }

            foreach(Oid oid in primaryworkitems) 
            {
                changeSet.AddAttributeValue(ChangeSetPrimaryWorkitemsDef, oid);
            }

            V1Connection.Data.Save(changeSet, changecomment);
            return changeSet;
        }

        private void SaveChangeSetLink(ChangeSetInfo info, Asset savedAsset) 
        {
            var name = string.Format(info.Link.Name, info.Revision);
            var url = string.Format(info.Link.Url, info.Revision);
            var linkUrlAttribute = savedAsset.GetAttribute(ChangeSetType.GetAttributeDefinition("Links.URL"));

            if(linkUrlAttribute != null) 
            {
                foreach(string value in linkUrlAttribute.Values) 
                {
                    if(value.Equals(url, StringComparison.InvariantCultureIgnoreCase)) 
                    {
                        return;
                    }
                }
            }

            var newlink = V1Connection.Data.New(LinkType, savedAsset.Oid.Momentless);
            newlink.SetAttributeValue(LinkNameDef, name);
            newlink.SetAttributeValue(LinkUrlDef, url);
            newlink.SetAttributeValue(LinkOnMenuDef, info.Link.OnMenu);

            V1Connection.Data.Save(newlink, changecomment);
        }

        #region Meta Properties

        private static readonly NeededAssetType[] neededassettypes =
            {
                new NeededAssetType("ChangeSet", new[] {"PrimaryWorkitems", "Name", "Reference", "Description"}),
                new NeededAssetType("PrimaryWorkitem", new string[] {}),
                new NeededAssetType("Link", new[] {"Name", "URL", "OnMenu"}),
            };

        private IAssetType ChangeSetType { get { return V1Connection.Meta.GetAssetType("ChangeSet"); } }
        private IAttributeDefinition ChangeSetPrimaryWorkitemsDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet.PrimaryWorkitems"); } }
        private IAttributeDefinition ChangeSetNameDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet.Name"); } }
        private IAttributeDefinition ChangeSetReferenceDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet.Reference"); } }
        private IAttributeDefinition ChangeSetDescriptionDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet.Description"); } }
        private IAttributeDefinition ChangeSetRepositoryIdDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet." + customRepositoryIdField); } }
        private IAttributeDefinition ChangeSetRepositoryFriendlyNameDef { get { return V1Connection.Meta.GetAttributeDefinition("ChangeSet." + customRepositoryFriendlyNameField); } }
        private IAssetType PrimaryWorkitemType { get { return V1Connection.Meta.GetAssetType("PrimaryWorkitem"); } }
        private IAttributeDefinition PrimaryWorkitemReferenceDef { get { return V1Connection.Meta.GetAttributeDefinition("PrimaryWorkitem.ChildrenMeAndDown." + referencename); } }
        private IAttributeDefinition LinkNameDef { get { return V1Connection.Meta.GetAttributeDefinition("Link.Name"); } }
        private IAttributeDefinition LinkUrlDef { get { return V1Connection.Meta.GetAttributeDefinition("Link.URL"); } }
        private IAttributeDefinition LinkOnMenuDef { get { return V1Connection.Meta.GetAttributeDefinition("Link.OnMenu"); } }
        private new string StoryName { get { return V1Connection.Localization.Resolve("Plural'Story"); } }
        private new string DefectName { get { return V1Connection.Localization.Resolve("Plural'Defect"); } }
        protected override IEnumerable<NeededAssetType> NeededAssetTypes { get { return neededassettypes; } }

        #endregion
    }
}