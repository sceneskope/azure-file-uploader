using CommandLineParser.Arguments;
using SceneSkope.Utilities.CommandLineApplications;

namespace UploadFiles
{
    internal class Arguments : ArgumentsBase
    {
        [ValueArgument(typeof(string), 'k', "key", Description = "Application insights key", Optional = true)]
        public override string Key { get; set; }

        [ValueArgument(typeof(string), 'q', "seqhost", Description = "Seq server host", Optional = true)]
        public override string SeqHost { get; set; }

        [ValueArgument(typeof(string), 'o', "seqtoken", Description = "Seq token", Optional = true)]
        public override string SeqToken { get; set; }

        [ValueArgument(typeof(string), 'l', "lockfile", Description = "Lock file", Optional = true)]
        public override string LockFile { get; set; }

        [SwitchArgument('h', "help", false, Description = "Show usage", Optional = true)]
        public override bool Help { get; set; }

        [SwitchArgument('c', "noconsole", false, Description = "No console", Optional = true)]
        public override bool NoConsole { get; set; }

        [ValueArgument(typeof(string), "input", Description = "Input directory", Optional = false)]
        public string InputDirectory { get; set; }

        [ValueArgument(typeof(string), "storageAccount", Description = "Storage account name", Optional = false)]
        public string AccountName { get; set; }

        [ValueArgument(typeof(string), "storageKeyValue", Description = "Storage key value", Optional = false)]
        public string KeyValue { get; set; }

        [ValueArgument(typeof(string), "storageContainer", Description = "Storage container name", Optional = false)]
        public string ContainerName { get; set; }

        [ValueArgument(typeof(string), "matchingRegex", Description = "Matching regular expression", Optional = false)]
        public string MatchingRegex { get; set; }

        [ValueArgument(typeof(string), "logfile", Description = "Log file name including the {Date} template", Optional = true)]
        public override string LogFile { get; set; }
    }
}
