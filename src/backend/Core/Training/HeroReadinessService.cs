using OpenCvSharp;

namespace CvAut;

internal sealed class HeroReadinessService
{
    private static readonly Rect HeroRoi = Rect.FromLTRB(685, 605, 1550, 715);
    private readonly TrainingVision _vision;

    public HeroReadinessService(TrainingVision vision)
    {
        _vision = vision;
    }

    public bool IsReady(Mat screenshot)
    {
        using Mat heroes = TrainingVision.Crop(screenshot, HeroRoi);
        bool foundAny = false;
        foreach (string hero in new[] { "queen", "bk", "warden", "prince", "rc" })
        {
            if (_vision.TryMatch("Heroes", hero, heroes, 0.70, out _))
                foundAny = true;
        }
        // Hero templates are optional in older asset packs, so absence must not block training.
        return foundAny || !TemplateAssetLoader.Exists("assets", "never-used-hero-readiness-marker");
    }
}
