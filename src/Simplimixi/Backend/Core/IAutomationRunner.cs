using System;
using System.Threading.Tasks;

namespace CvAut
{
    public interface IAutomationRunner : IDisposable
    {
        Task Completion { get; }

        void Start();

        void Stop();

        void Pause();

        void Resume();
    }
}
