using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using VersionOne.Profile;
using VersionOne.ServiceHost.Eventing;

namespace VersionOne.ServiceHost.SubversionServices
{
    public class SvnReaderHostedService : AbstractRevisionProcessor 
    {
        private const string LinkNode = "Link";
        private const string LinkName = "Name";
        private const string LinkUrl = "URL";
        private const string LinkOnMenu = "OnMenu";

        private const string RepositoryFriendlyNameSpecField = "FriendlyRepositoryNameSpec";
        private const string ReferenceExpressionField = "ReferenceExpression";

        private const string FriendlyFieldUrl = "#URL#";
        private const string FriendlyFieldPath = "#Path#";
        private const string FriendlyFieldRoot = "#Root#";
        private LinkInfo linkInfo;

        private string repositoryFriendlyNameSpec;
        //private string repositoryUuid;

        private SvnInformation svnInfo;

        protected string ReferenceExpression { get; set; }

        protected string RepositoryUuid 
        {
            get 
            {
                return String.Empty;

                //TO DO: Currently throws SVN error "cannot process more than one command". This makes the custom ChangeSet.RepositoryID field always blank.
                //if(string.IsNullOrEmpty(repositoryUuid)) 
                //{
                //    repositoryUuid = connector.GetRepositoryUuid(RepositoryPath);
                //}
                //return repositoryUuid;
            }
        }

        protected string RepositoryFriendlyName 
        {
            get 
            {
                switch(repositoryFriendlyNameSpec) 
                {
                    case FriendlyFieldUrl:
                        return RepositoryPath;
                    case FriendlyFieldPath:
                        return svnInfo.Path;
                    case FriendlyFieldRoot:
                        return svnInfo.Root;
                    default:
                        return repositoryFriendlyNameSpec;
                }
            }
        }

        protected override Type PubType 
        {
            get { return typeof(SvnReaderIntervalSync); }
        }

        protected override void InternalInitialize(XmlElement config, IEventManager eventmanager, IProfile profile) 
        {
            ReferenceExpression = config[ReferenceExpressionField].InnerText;
            repositoryFriendlyNameSpec = config[RepositoryFriendlyNameSpecField].InnerText;
            LoadLinkInfo(config[LinkNode]);

            svnInfo = connector.GetSvnInformation(RepositoryPath);
        }

        protected override void InternalDispose(bool deterministic) { }

        protected override void ProcessRevision(int revision, string author, DateTime changeDate, string message, IList<string> filesChanged, ChangeSetDictionary changedPathInfos) 
        {
            List<string> references = GetReferences(message);

            var changeSet = new ChangeSetInfo(author, message, filesChanged, revision, RepositoryUuid, changeDate, references, linkInfo, RepositoryFriendlyName);

            var referenceStrings = new string[changeSet.References.Count];
            changeSet.References.CopyTo(referenceStrings, 0);
            string referenceMessage = (referenceStrings.Length > 0) ? "references: " + string.Join(", ", referenceStrings) : "No References found.";
            Logger.Log(string.Format("Publishing ChangeSet: {0}, \"{1}\"; {2}", changeSet.Revision, changeSet.Message, referenceMessage));
            PublishChangeSet(changeSet);

            base.ProcessRevision(revision, author, changeDate, message, filesChanged, changedPathInfos);
        }

        protected virtual void PublishChangeSet(ChangeSetInfo changeSet) 
        {
            EventManager.Publish(changeSet);
        }

        private List<string> GetReferences(string message) 
        {
            var result = new List<string>();
            var expression = new Regex(ReferenceExpression);

            foreach(Match match in expression.Matches(message)) 
            {
                result.Add(match.Value);
            }

            return result;
        }

        private void LoadLinkInfo(XmlElement configLinkRoot) 
        {
            if(configLinkRoot != null) 
            {
                XmlElement namenode = configLinkRoot[LinkName];
                XmlElement linknode = configLinkRoot[LinkUrl];
                XmlElement onmenunode = configLinkRoot[LinkOnMenu];

                if(namenode != null && linknode != null && onmenunode != null) 
                {
                    string name = namenode.InnerText;
                    string url = linknode.InnerText;
                    bool onmenu;

                    if(!bool.TryParse(onmenunode.InnerText, out onmenu)) 
                    {
                        onmenu = false;
                    }

                    if(!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url)) 
                    {
                        linkInfo = new LinkInfo(name, url, onmenu);
                    }
                }
            }
        }

        #region Nested type: SvnReaderIntervalSync

        public class SvnReaderIntervalSync {}

        #endregion
    }
}