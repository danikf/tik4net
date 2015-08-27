using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public class LoadingContext
    {
        private readonly ITikCommand _loadingCommand;

        public bool IsRunning
        {
            get { return _loadingCommand.IsRunning; }
        }

        internal LoadingContext(ITikCommand loadingCommand)
        {
            Guard.ArgumentNotNull(loadingCommand, "loadingCommand");

            _loadingCommand = loadingCommand;
        }

        public void Cancel()
        {
            _loadingCommand.Cancel();
        }
    }
}
