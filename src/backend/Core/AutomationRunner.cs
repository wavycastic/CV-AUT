using System.Threading.Tasks;

namespace CvAut
{
    public sealed class AutomationRunner : IAutomationRunner
    {
        private readonly CVAutomationFramework _framework;

        public AutomationRunner(string configPath)
        {
            _framework = new CVAutomationFramework(configPath);
        }

        public Task Completion => _framework.Completion;

        public void Start() => _framework.Start();

        public void Stop() => _framework.Stop();

        public void Pause() => _framework.Pause();

        public void Resume() => _framework.Resume();

        public void Dispose() => _framework.Dispose();
    }
}
