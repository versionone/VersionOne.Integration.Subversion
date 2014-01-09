using System;
using System.Collections.Generic;
using System.Xml;
using VersionOne.Profile;
using VersionOne.ServiceHost.Core.Logging;
using VersionOne.ServiceHost.Core.Services;
using VersionOne.ServiceHost.Eventing;

namespace VersionOne.ServiceHost.SubversionServices 
{
    public abstract class AbstractRevisionProcessor : IDisposable, IHostedService 
    {
        private readonly object _lock = new object();
        protected readonly SvnConnector connector = new SvnConnector();
        protected IEventManager EventManager;
        private IProfile profile;
        protected ILogger Logger;
        private string password;
        private string repositoryPath;
        private string username;

        protected virtual int LastRevision 
        {
            get 
            {
                int rev;
                int.TryParse(profile["Revision"].Value, out rev);
                return rev;
            }
            set 
            { 
                profile["Revision"].Value = value.ToString(); 
            }
        }

        protected abstract Type PubType { get; }

        protected SvnConnector Connector 
        {
            get { return connector; }
        }

        protected string RepositoryPath 
        {
            get { return repositoryPath; }
        }

        public void Initialize(XmlElement config, IEventManager eventManager, IProfile profile) 
        {
            this.profile = profile;

            repositoryPath = config["RepositoryPath"].InnerText;
            username = (config["UserName"] != null) ? config["UserName"].InnerText : string.Empty;
            password = (config["Password"] != null) ? config["Password"].InnerText : string.Empty;

            EventManager = eventManager;
            EventManager.Subscribe(PubType, PokeRepository);

            Logger = new Logger(eventManager);

            if(!string.IsNullOrEmpty(username) && password != null) 
            {
                connector.SetAuthentication(username, password);
            }

            connector.Revision += _connector_Revision;
            connector.Error += _connector_Error;

            InternalInitialize(config, eventManager, profile);
        }

        public void Start() 
        {
            // TODO move subscriptions to timer events, etc. here
        }

        protected abstract void InternalInitialize(XmlElement config, IEventManager eventmanager, IProfile profile);

        private void _connector_Error(object sender, SvnExceptionEventArgs e) 
        {
            var errorString = string.Format(
                "Error accessing Subversion repository: Path='{0}', Username='{1}', Password='{2}'. " +
                    "The service will be disabled until ServiceHost is restarted.", repositoryPath, username, password);
            Logger.Log(errorString, e.Exception);
            EventManager.Unsubscribe(PubType, PokeRepository);
        }

        protected void _connector_Revision(object sender, SvnConnector.RevisionArgs e) 
        {
            if(e.Revision > LastRevision) 
            {
                lock(_lock) 
                {
                    if(e.Revision > LastRevision) 
                    {
                        ProcessRevision(e.Revision, e.Author, e.Time, e.Message, e.Changed, e.ChangePathInfos);
                        return;
                    }
                }
            }
            Logger.Log(LogMessage.SeverityType.Debug, string.Format("Ignoring revision {0} - already processed.", e.Revision));
        }

        private void PokeRepository(object pubobj) 
        {
            connector.Poke(RepositoryPath, LastRevision);
        }

        protected virtual void ProcessRevision(int revision, string author, DateTime changeDate, string message, IList<string> filesChanged, ChangeSetDictionary changedPathInfos) 
        {
            LastRevision = revision;
        }

        #region Disposal

        public void Dispose() 
        {
            Dispose(true);
        }

        //True if disposal is deterministic, meaning we should dispose managed objects.
        private void Dispose(bool deterministic) 
        {
            if(deterministic) 
            {
                connector.Dispose();
            }
            InternalDispose(deterministic);
        }

        protected abstract void InternalDispose(bool deterministic);

        ~AbstractRevisionProcessor() 
        {
            // When we are GC'd, we don't dispose managed objects - we let the GC handle that
            Dispose(false);
        }

        #endregion
    }
}