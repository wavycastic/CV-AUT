using System;
using System.Threading;
using CvAut.Configuration;

namespace CvAut.Automation;

internal sealed class AccountRotationLoop
{
    private readonly IConfigService _configService;
    private readonly AccountSwitcher _accounts;
    private readonly WallUpdater _wallUpdater;

    public AccountRotationLoop(
        IConfigService configService,
        AccountSwitcher accounts,
        WallUpdater wallUpdater)
    {
        _configService = configService;
        _accounts = accounts;
        _wallUpdater = wallUpdater;
    }

    public void Run(
        ref int currentVillageIdx,
        ref bool fastAttackQueued,
        ref int cycleCount,
        ref int sessionBattlesCompleted,
        Func<CancellationToken, bool> checkStopFunc,
        Action waitIfPausedFunc,
        Func<int, CancellationToken, bool> interruptibleSleepFunc,
        Action<CancellationToken> oneCycleFunc,
        CancellationToken token)
    {
        Console.WriteLine("[DEBUG][FSM-CS] phase=worker_loop status=start");

        MultiAccountConfig multiConfig = _configService.Current.MultiAccount;
        bool enableMulti = multiConfig.Enabled;

        if (!enableMulti)
        {
            Console.WriteLine("[DEBUG][FSM-CS] phase=worker_loop status=pending mode=single_account");
            currentVillageIdx = 1;
            while (!checkStopFunc(token))
            {
                oneCycleFunc(token);
                if (checkStopFunc(token)) break;
                interruptibleSleepFunc(fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : AutomationThresholds.NormalCycleDelayMs, token);
            }
            return;
        }

        AccountConfig[] accounts = _accounts.GetConfiguredAccounts(default);
        int intervalSecs = Math.Max(1, multiConfig.IntervalMinutes) * 60;
        bool switchByMinutes = multiConfig.SwitchAfterMinutesEnabled;
        bool switchByBattles = multiConfig.SwitchAfterBattlesEnabled;
        int battleLimit = multiConfig.SwitchAfterBattles;
        bool switchByClanPoints = multiConfig.SwitchAfterClanPointsEnabled;
        int clanPointLimit = multiConfig.SwitchAfterClanPoints;

        while (!checkStopFunc(token))
        {
            foreach (AccountConfig account in accounts)
            {
                waitIfPausedFunc();
                if (checkStopFunc(token)) break;

                int idx = account.ProfileVillage;
                currentVillageIdx = idx;
                fastAttackQueued = false;
                Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_account target={idx} account=\"{account.Name}\"");

                if (!_accounts.SwitchToAccount(account, token))
                {
                    Console.WriteLine($"[FSM-CS WARNING] phase=account_switch status=fail target={idx} account=\"{account.Name}\" action=skip_account");
                    continue;
                }
                _wallUpdater.ResetSavedOffset();

                DateTime slotStart = DateTime.Now;
                int slotBattleStart = sessionBattlesCompleted;
                int slotClanPointStart = ConfigService.ReadClanGamesPoints(idx);
                cycleCount = 0;

                string switchReason = "none";
                while (!_accounts.ShouldSwitchAccount(
                    slotStart,
                    slotBattleStart,
                    slotClanPointStart,
                    idx,
                    switchByMinutes,
                    intervalSecs,
                    switchByBattles,
                    battleLimit,
                    switchByClanPoints,
                    clanPointLimit,
                    sessionBattlesCompleted,
                    out switchReason) && !checkStopFunc(token))
                {
                    waitIfPausedFunc();
                    oneCycleFunc(token);
                    if (checkStopFunc(token)) break;
                    interruptibleSleepFunc(fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : 15000, token);
                }

                Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_account target={idx} outcome=next reason={switchReason}");
            }

            interruptibleSleepFunc(5000, token);
        }

        Console.WriteLine("[FSM-CS] phase=worker_loop status=stopped");
    }
}
