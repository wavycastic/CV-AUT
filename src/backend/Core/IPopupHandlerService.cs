using System;

namespace CvAut;

public interface IPopupHandlerService
{
    bool IsHandlingConnectionPopup { get; }
    bool HandleBlockingConnectionPopup(string warningMessage, Func<bool>? reloadAction = null, bool disableDialogShapeFallback = false);
    bool ConnectionPopupVisible(out string matchInfo, bool allowDialogShapeFallback = true);
    bool HandleTreasureHuntIfPresent(bool verboseNotFound = true);
    bool DismissStarBonusIfPresent();
}
