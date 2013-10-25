using System;

namespace VersionOne.ServiceHost.SubversionServices
{
    public class SvnExceptionEventArgs : EventArgs 
    {
        public readonly Exception Exception;

        public readonly string RepositoryPath;
        public readonly string Username;
        public readonly string Password;

        public SvnExceptionEventArgs(Exception exception) 
        {
            Exception = exception;
        }

        public SvnExceptionEventArgs(Exception exception, string path, string username, string password) : this(exception)
        {
            RepositoryPath = path;
            Username = username;
            Password = password;
        }
    }
}