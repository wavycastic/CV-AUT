namespace CvAut.Configuration;

public interface IConfigSnapshotProvider
{
    AutomationConfigSnapshot Current { get; }
    void Reload();
}
