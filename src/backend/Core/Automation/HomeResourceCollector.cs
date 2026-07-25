using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal sealed class HomeResourceCollector
{
    private readonly IADBHelper _adb;
    private readonly PopupHandlerService _popups;
    private readonly string _templatesPath;

    public HomeResourceCollector(IADBHelper adb, PopupHandlerService popups, string templatesPath)
    {
        _adb = adb;
        _popups = popups;
        _templatesPath = templatesPath;
    }

    public bool CollectResources(
        Func<int, CancellationToken, bool> sleepFunc,
        CancellationToken token)
    {
        if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup before collect → reload"))
        {
            return true;
        }

        string[] collectorTemplates =
        {
            @"resources\elixir_collector.png",
            @"resources\DE_collector.png",
            @"resources\gold_collector.png"
        };

        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            Console.WriteLine("[FSM-CS WARNING] phase=collect_resources status=fail reason=screenshot_failed");
            return false;
        }

        using Mat grayScreenshot = new Mat();
        Cv2.CvtColor(screenshot, grayScreenshot, ColorConversionCodes.BGR2GRAY);

        foreach (string templateName in collectorTemplates)
        {
            if (!TemplateAssetLoader.Exists(_templatesPath, templateName))
            {
                Console.WriteLine($"[VISION] phase=collect_resources status=fail reason=template_missing details=\"{templateName}\"");
                continue;
            }

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup during collect → reload"))
            {
                return true;
            }

            using Mat template = TemplateAssetLoader.Load(_templatesPath, templateName, ImreadModes.Grayscale);
            if (template.Empty())
            {
                Console.WriteLine($"[VISION] phase=collect_resources status=fail reason=template_unreadable details=\"{templateName}\"");
                continue;
            }

            using Mat result = new Mat();
            Cv2.MatchTemplate(grayScreenshot, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

            if (maxVal < 0.65)
            {
                Console.WriteLine($"[FSM-CS] phase=collect_resources status=skip item=\"{templateName}\" reason=below_threshold");
                continue;
            }

            int centerX = maxLoc.X + template.Width / 2;
            int centerY = maxLoc.Y + template.Height / 2;
            Console.WriteLine($"[FSM-CS] phase=collect_resources status=success item=\"{templateName}\"");
            _adb.Tap(centerX, centerY);
            if (sleepFunc(500, token))
            {
                return true;
            }
        }

        return false;
    }
}
