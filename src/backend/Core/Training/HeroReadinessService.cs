using OpenCvSharp;

namespace CvAut;

internal sealed class HeroReadinessService
{
    private static readonly Rect HeroRoi = Rect.FromLTRB(685, 605, 1550, 715);
    private static readonly string[] HeroTemplates =
    {
        "queen",
        "bk",
        "warden",
        "prince",
        "rc"
    };

    private readonly TrainingVision _vision;

    public HeroReadinessService(TrainingVision vision)
    {
        _vision = vision;
    }

    public bool IsReady(Mat screenshot)
    {
        using Mat heroes = TrainingVision.Crop(screenshot, HeroRoi);
        bool hasSupportedTemplate = false;
        foreach (string hero in HeroTemplates)
        {
            if (!_vision.TemplateExists("Heroes", hero)) continue;
            hasSupportedTemplate = true;
            if (_vision.TryMatch("Heroes", hero, heroes, 0.70, out _))
                return true;
        }

        // Older asset packs have no hero readiness templates. Preserve the legacy
        // non-blocking behavior until those assets are available.
        return !hasSupportedTemplate;
    }
}
