using System.IO;
using System.Text.Json.Nodes;
using CvAut;
using CvAut.Models;
using CvAut.Services.Emulators;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using Xunit;

namespace CvAut.Tests
{
    public class AdbEndpointTests
    {
        [Theory]
        [InlineData("127.0.0.1:5556", "127.0.0.1", 5556)]
        [InlineData("localhost:21503", "localhost", 21503)]
        [InlineData("emulator-5554", "127.0.0.1", 5554)]
        public void TryParse_ValidForms(string serial, string host, int port)
        {
            Assert.True(CvAut.Services.Emulators.AdbEndpoint.TryParse(serial, out string h, out int p));
            Assert.Equal(host, h);
            Assert.Equal(port, p);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nonsense")]
        [InlineData("host:notaport")]
        public void TryParse_InvalidForms(string serial)
        {
            Assert.False(CvAut.Services.Emulators.AdbEndpoint.TryParse(serial, out _, out _));
        }
    }

    public class PlayModeTests
    {
        [Theory]
        [InlineData("main_village", PlayMode.MainVillageLabel)]
        [InlineData("night_village", PlayMode.NightVillageLabel)]
        [InlineData("clan_games", PlayMode.ClanGamesLabel)]
        [InlineData("clan_capital", PlayMode.ClanCapitalLabel)]
        [InlineData("unknown", PlayMode.MainVillageLabel)]
        [InlineData(null, PlayMode.MainVillageLabel)]
        public void ToDisplay_MapsTokens(string? token, string expected)
            => Assert.Equal(expected, PlayMode.ToDisplay(token));

        [Theory]
        [InlineData(PlayMode.MainVillageLabel, "main_village")]
        [InlineData(PlayMode.NightVillageLabel, "night_village")]
        [InlineData(PlayMode.ClanGamesLabel, "clan_games")]
        [InlineData(PlayMode.ClanCapitalLabel, "clan_capital")]
        [InlineData("something else", "main_village")]
        public void ToToken_MapsLabels(string label, string expected)
            => Assert.Equal(expected, PlayMode.ToToken(label));

        [Theory]
        [InlineData("main_village")]
        [InlineData("night_village")]
        [InlineData("clan_games")]
        [InlineData("clan_capital")]
        public void RoundTrip_TokenLabelToken(string token)
            => Assert.Equal(token, PlayMode.ToToken(PlayMode.ToDisplay(token)));
    }

    public class DeviceProfileKeyTests
    {
        [Fact]
        public void ProfileKey_DerivesFromEndpointNotDisplayName()
        {
            var a = new Device("127.0.0.1", 5556, "BlueStacks", "BlueStacks", DeviceStatus.Ready);
            var b = new Device("127.0.0.1", 5556, "Different Name", "LDPlayer", DeviceStatus.Offline);
            Assert.Equal("device_127.0.0.1_5556", a.ProfileKey);
            Assert.Equal(a.ProfileKey, b.ProfileKey);
        }
    }

    public class ConfigStoreTests
    {
        private static ConfigStore NewIsolatedStore()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_test_" + System.Guid.NewGuid().ToString("N"));
            return new ConfigStore(root);
        }

        [Fact]
        public void DefaultProfile_AlwaysPresent()
        {
            var store = NewIsolatedStore();
            Assert.Contains(store.Profiles, p => p.Name == "Default");
            Assert.Equal("Default", store.ActiveProfileName);
        }

        [Fact]
        public void SaveProfileAs_ThenLoad_RoundTrips()
        {
            var store = NewIsolatedStore();
            var cfg = new JsonObject { ["play_mode"] = "night_village" };
            store.SaveProfileAs("device_127.0.0.1_5556", cfg);

            Assert.Equal("device_127.0.0.1_5556", store.ActiveProfileName);
            Assert.Contains(store.Profiles, p => p.Name == "device_127.0.0.1_5556");

            store.LoadProfile("device_127.0.0.1_5556");
            JsonObject loaded = store.LoadActiveConfig();
            Assert.Equal("night_village", loaded["play_mode"]!.ToString());
        }

        [Fact]
        public void DeleteProfile_RemovesIt_AndFallsBackToDefault()
        {
            var store = NewIsolatedStore();
            store.SaveProfileAs("temp_profile", new JsonObject());
            Assert.Contains(store.Profiles, p => p.Name == "temp_profile");

            store.DeleteProfile("temp_profile");
            Assert.DoesNotContain(store.Profiles, p => p.Name == "temp_profile");
            Assert.Equal("Default", store.ActiveProfileName);
        }

        [Fact]
        public void DeleteProfile_Default_IsNoOp()
        {
            var store = NewIsolatedStore();
            store.DeleteProfile("Default");
            Assert.Contains(store.Profiles, p => p.Name == "Default");
        }

        [Fact]
        public void NightVillage_DefaultsAndViewModel_RoundTrip()
        {
            var store = NewIsolatedStore();
            JsonObject config = store.LoadActiveConfig();
            var night = Assert.IsType<JsonObject>(config["night_village"]);
            Assert.Equal("1", night["attack_count"]!.ToString());
            Assert.Equal("true", night["enable_attack"]!.ToString().ToLowerInvariant());
            Assert.Equal("false", night["boost_clock_tower"]!.ToString().ToLowerInvariant());
            Assert.Equal("true", night["army_management"]!.ToString().ToLowerInvariant());
            Assert.Equal("auto", night["army_formation"]!.ToString());
            Assert.Equal("fixed", night["attack_count_mode"]!.ToString());
            Assert.Equal("true", night["enable_stage2"]!.ToString().ToLowerInvariant());

            var vm = new NightVillageViewModel(store)
            {
                AttackCount = 3,
                EnableAttack = true,
                BoostClockTower = true,
                UpgradeWall = true,
                FillArmy = true,
                ArmyFormation = "power_pekka",
                WaitForHeroes = true,
                HeroWaitSeconds = 120,
                AttackCountMode = "trophy",
                CustomDropOrderEnabled = true,
                DropOrder = "BattleMachine|Bomber|PowerPekka",
                NextTroopDelayMs = 700,
                SameTroopDelayMs = 220
            };
            vm.ApplyTo(config);

            night = Assert.IsType<JsonObject>(config["night_village"]);
            Assert.Equal("3", night["attack_count"]!.ToString());
            Assert.Equal("true", night["boost_clock_tower"]!.ToString().ToLowerInvariant());
            Assert.Equal("true", night["upgrade_wall"]!.ToString().ToLowerInvariant());
            Assert.Equal("true", night["fill_army"]!.ToString().ToLowerInvariant());
            Assert.Equal("power_pekka", night["army_formation"]!.ToString());
            Assert.Equal("120", night["hero_wait_seconds"]!.ToString());
            Assert.Equal("trophy", night["attack_count_mode"]!.ToString());
            Assert.Equal("true", night["custom_drop_order_enabled"]!.ToString().ToLowerInvariant());
            Assert.Equal("BattleMachine|Bomber|PowerPekka", night["drop_order"]!.ToString());
            Assert.Equal("700", night["next_troop_delay_ms"]!.ToString());
            Assert.Equal("220", night["same_troop_delay_ms"]!.ToString());
        }
    }


    public class DeviceSessionManagerTests
    {
        private static Device Dev(int port)
            => new Device("127.0.0.1", port, "Mock" + port, "Mock", DeviceStatus.Ready);

        [Fact]
        public void Sessions_IncludesEveryCreatedDevice_NotJustActive()
        {
            using var mgr = new CvAut.Services.Sessions.DeviceSessionManager();
            var s1 = mgr.GetOrCreate(Dev(5556), "mock");
            var s2 = mgr.GetOrCreate(Dev(5558), "mock");
            var s3 = mgr.GetOrCreate(Dev(5560), "mock");

            Assert.Equal(3, mgr.Sessions.Count);
            Assert.Contains(s1, mgr.Sessions);
            Assert.Contains(s2, mgr.Sessions);
            Assert.Contains(s3, mgr.Sessions);
        }

        [Fact]
        public void GetOrCreate_SameDevice_ReturnsSameSession()
        {
            using var mgr = new CvAut.Services.Sessions.DeviceSessionManager();
            var a = mgr.GetOrCreate(Dev(5556), "mock");
            var b = mgr.GetOrCreate(Dev(5556), "mock");
            Assert.Same(a, b);
            Assert.Single(mgr.Sessions);
        }

        [Fact]
        public void Remove_DropsSessionFromList()
        {
            using var mgr = new CvAut.Services.Sessions.DeviceSessionManager();
            mgr.GetOrCreate(Dev(5556), "mock");
            mgr.GetOrCreate(Dev(5558), "mock");
            mgr.Remove("127.0.0.1:5556");
            Assert.Single(mgr.Sessions);
        }
    }


    public class DashboardVisibilityTests
    {
        private static DashboardViewModel NewDashboard()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_dash_" + System.Guid.NewGuid().ToString("N"));
            return new DashboardViewModel(new SettingsViewModel(), new ConfigStore(root));
        }

        private static DeviceViewModel Dev(int port)
            => new DeviceViewModel(new Device("127.0.0.1", port, "Mock" + port, "Mock", DeviceStatus.Ready));

        [Fact]
        public void SingleMode_WithActiveDevice_ShowsActivePanel()
        {
            var d = NewDashboard();
            d.IsGridMode = false;
            d.ActiveDevice = Dev(5556);
            d.State = DashboardDeviceState.DeviceSelected;

            Assert.True(d.ShowActivePanel);
            Assert.False(d.ShowSelectionPane);
            Assert.False(d.ShowGridPane);
        }

        [Fact]
        public void GridMode_HidesActivePanel_AndSelectionPane()
        {
            var d = NewDashboard();
            d.AttachDevices(new System.Collections.ObjectModel.ObservableCollection<DeviceViewModel> { Dev(5556), Dev(5558) });
            d.ActiveDevice = Dev(5556);
            d.State = DashboardDeviceState.DeviceSelected;
            d.IsGridMode = true;
            d.NotifyDevicesChanged();

            Assert.False(d.ShowActivePanel);
            Assert.False(d.ShowSelectionPane);
            Assert.True(d.ShowGridPane);
        }

        [Fact]
        public void GridMode_NoDevices_ShowsEmptySelectionPane()
        {
            var d = NewDashboard();
            d.AttachDevices(new System.Collections.ObjectModel.ObservableCollection<DeviceViewModel>());
            d.IsGridMode = true;
            d.State = DashboardDeviceState.NoDevices;
            d.NotifyDevicesChanged();

            Assert.False(d.ShowGridPane);
            Assert.True(d.ShowSelectionPane);
            Assert.True(d.ShowEmptyState);
        }

        [Fact]
        public void Configuring_TakesPrecedenceOverGrid()
        {
            var d = NewDashboard();
            d.AttachDevices(new System.Collections.ObjectModel.ObservableCollection<DeviceViewModel> { Dev(5556) });
            d.IsGridMode = true;
            d.State = DashboardDeviceState.ConfiguringDevice;
            d.NotifyDevicesChanged();

            Assert.True(d.ShowConfiguringPanel);
            Assert.False(d.ShowGridPane);
        }

        [Fact]
        public void DetectDetail_AppendedToNoDevicesStatus()
        {
            var d = NewDashboard();
            d.State = DashboardDeviceState.NoDevices;
            d.DetectDetail = "ADB offline";

            Assert.Contains("ADB offline", d.StatusText);
        }
    }


    public class AppLogContextTests
    {
        [Fact]
        public void LineWrittenWithContext_CarriesAmbientDeviceId()
        {
            string? seenLine = null;
            string? seenCtx = "unset";
            Action<string, string?> handler = (line, ctx) => { seenLine = line; seenCtx = ctx; };
            AppLog.LineWrittenWithContext += handler;
            try
            {
                AppLog.DeviceContext.Value = "127.0.0.1:5556";
                AppLog.Raise("[FSM-CS] phase=home_check");
                Assert.Equal("[FSM-CS] phase=home_check", seenLine);
                Assert.Equal("127.0.0.1:5556", seenCtx);
            }
            finally
            {
                AppLog.LineWrittenWithContext -= handler;
                AppLog.DeviceContext.Value = null;
            }
        }

        [Fact]
        public async Task DeviceContext_FlowsAcrossTaskRun()
        {
            string? captured = "unset";
            Action<string, string?> handler = (line, ctx) => captured = ctx;
            AppLog.LineWrittenWithContext += handler;
            try
            {
                await Task.Run(async () =>
                {
                    AppLog.DeviceContext.Value = "emu:5560";
                    // nested task inherits the ambient context via ExecutionContext flow
                    await Task.Run(() => AppLog.Raise("nested worker line"));
                });

                Assert.Equal("emu:5560", captured);
            }
            finally
            {
                AppLog.LineWrittenWithContext -= handler;
                AppLog.DeviceContext.Value = null;
            }
        }

        [Fact]
        public async Task ConcurrentContexts_DoNotBleed()
        {
            var seen = new System.Collections.Concurrent.ConcurrentBag<string?>();
            Action<string, string?> handler = (line, ctx) => seen.Add(ctx);
            AppLog.LineWrittenWithContext += handler;
            try
            {
                var t1 = Task.Run(() => { AppLog.DeviceContext.Value = "A"; AppLog.Raise("a"); });
                var t2 = Task.Run(() => { AppLog.DeviceContext.Value = "B"; AppLog.Raise("b"); });
                await Task.WhenAll(t1, t2);

                Assert.Contains("A", seen);
                Assert.Contains("B", seen);
                Assert.Equal(2, seen.Count);
            }
            finally
            {
                AppLog.LineWrittenWithContext -= handler;
            }
        }
    }

    public class LogEntryDisplayTests
    {
        [Fact]
        public void Summary_IncludesBotInputFieldsForDebugging()
        {
            var entry = new LogEntry("[ADB] phase=input status=send action=bot_swipe x1=1 y1=2 x2=3 y2=4 duration_ms=300", LogLevel.Info, "emu:5556");

            Assert.Contains("nhập liệu", entry.Summary);
            Assert.Contains("Bot_swipe", entry.Summary);
            Assert.Contains("emu:5556", entry.SearchText);
        }

        [Fact]
        public void Summary_TranslatesStatusAndCommonMessages()
        {
            var entry1 = new LogEntry("[WALL] phase=read_resources status=success reason=unsupported_wall_level", LogLevel.Info);
            Assert.Contains("Đọc tài nguyên", entry1.Summary);
            Assert.Contains("thành công", entry1.Summary);
            Assert.Contains("cấp tường không hỗ trợ", entry1.Summary);

            var entry2 = new LogEntry("Screenshot failed", LogLevel.Error);
            Assert.Contains("[LỖI] Chụp màn hình giả lập thất bại.", entry2.Summary);
        }
    }


    public class PrepareDeviceConfigTests
    {
        private static ConfigStore NewIsolatedStore()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_prep_" + System.Guid.NewGuid().ToString("N"));
            return new ConfigStore(root);
        }

        [Fact]
        public void PrepareDeviceConfig_WritesOwnHostPort()
        {
            var store = NewIsolatedStore();
            string path = store.PrepareDeviceConfig("device_127.0.0.1_5558", "127.0.0.1", 5558);

            Assert.True(File.Exists(path));
            var cfg = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            var dev = (System.Text.Json.Nodes.JsonObject)cfg["device_connection"]!;
            Assert.Equal("127.0.0.1", dev["host"]!.ToString());
            Assert.Equal(5558, int.Parse(dev["port"]!.ToString()));
        }

        [Fact]
        public void PrepareDeviceConfig_DistinctDevices_DistinctFilesAndPorts()
        {
            var store = NewIsolatedStore();
            string p1 = store.PrepareDeviceConfig("device_127.0.0.1_5556", "127.0.0.1", 5556);
            string p2 = store.PrepareDeviceConfig("device_127.0.0.1_5558", "127.0.0.1", 5558);

            Assert.NotEqual(p1, p2);
            var c1 = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p1))!;
            var c2 = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p2))!;
            Assert.Equal(5556, int.Parse(((System.Text.Json.Nodes.JsonObject)c1["device_connection"]!)["port"]!.ToString()));
            Assert.Equal(5558, int.Parse(((System.Text.Json.Nodes.JsonObject)c2["device_connection"]!)["port"]!.ToString()));
        }

        [Fact]
        public void PrepareDeviceConfig_DoesNotChangeActiveProfile()
        {
            var store = NewIsolatedStore();
            string before = store.ActiveProfileName;
            store.PrepareDeviceConfig("device_127.0.0.1_5560", "127.0.0.1", 5560);
            Assert.Equal(before, store.ActiveProfileName);
        }
    }


    public class TopBarAggregateTests
    {
        private static TopBarViewModel NewTopBar()
            => new TopBarViewModel(new CvAut.Services.AppStateService(), new CvAut.Services.Sessions.DeviceSessionManager(), new ConfigStore(Path.Combine(Path.GetTempPath(), "cvaut_tb_" + System.Guid.NewGuid().ToString("N"))));

        private static DeviceViewModel DevWithStats(int port, int battles, int stars, long gold)
        {
            var d = new DeviceViewModel(new Device("127.0.0.1", port, "Mock" + port, "Mock", DeviceStatus.Ready));
            d.Stats.Apply(new CvAut.Models.SessionStats { Battles = battles, Stars = stars, Gold = gold });
            return d;
        }

        [Fact]
        public void RefreshAggregate_SumsLootAcrossDevices()
        {
            var tb = NewTopBar();
            tb.RefreshAggregate(new[] { DevWithStats(5556, 2, 4, 1000), DevWithStats(5558, 3, 5, 2500) });

            Assert.Equal(5, tb.TotalBattles);
            Assert.Equal(9, tb.TotalStars);
            Assert.Equal(3500, tb.TotalGold);
        }

        [Fact]
        public void RefreshAggregate_CountsErrorDevices()
        {
            var tb = NewTopBar();
            var err = new DeviceViewModel(new Device("127.0.0.1", 5560, "Mock", "Mock", DeviceStatus.Ready));
            err.Status = BotStatus.Error;
            tb.RefreshAggregate(new[] { DevWithStats(5556, 1, 1, 100), err });

            Assert.Equal(1, tb.ErrorCount);
            Assert.True(tb.HasErrors);
        }

        [Fact]
        public void RefreshAggregate_NoErrors_HasErrorsFalse()
        {
            var tb = NewTopBar();
            tb.RefreshAggregate(new[] { DevWithStats(5556, 1, 1, 100) });
            Assert.False(tb.HasErrors);
        }
    }


    public class NotificationTests
    {
        [Fact]
        public void IsActionable_RequiresEnabledAndHttpsUrl()
        {
            Assert.False(new NotificationSettings { Enabled = false, WebhookUrl = "https://x" }.IsActionable);
            Assert.False(new NotificationSettings { Enabled = true, WebhookUrl = "" }.IsActionable);
            Assert.False(new NotificationSettings { Enabled = true, WebhookUrl = "ftp://x" }.IsActionable);
            Assert.True(new NotificationSettings { Enabled = true, WebhookUrl = "https://discord.com/api/webhooks/1/abc" }.IsActionable);
        }

        [Fact]
        public void ShouldNotify_RespectsPerEventToggles()
        {
            var s = new NotificationSettings { NotifyOnError = true, NotifyOnStopped = false, NotifyOnStarted = false };
            Assert.True(s.ShouldNotify(BotStatus.Error));
            Assert.False(s.ShouldNotify(BotStatus.Stopped));
            Assert.False(s.ShouldNotify(BotStatus.Running));
            Assert.False(s.ShouldNotify(BotStatus.Paused));
        }

        [Fact]
        public void FormatMessage_IncludesDeviceStatusAndDetail()
        {
            string msg = CvAut.Services.Notifications.DiscordWebhookNotificationService.FormatMessage("BlueStacks:5556", BotStatus.Error, "ADB offline");
            Assert.Contains("BlueStacks:5556", msg);
            Assert.Contains("ADB offline", msg);
        }

        [Fact]
        public async Task Notify_NoOp_WhenNotActionable_DoesNotThrow()
        {
            var svc = new CvAut.Services.Notifications.DiscordWebhookNotificationService(() => new NotificationSettings());
            await svc.NotifyStatusAsync("dev", BotStatus.Error); // disabled -> no HTTP, no throw
        }

        [Fact]
        public void ConfigStore_NotificationSettings_RoundTrip()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_notif_" + System.Guid.NewGuid().ToString("N"));
            var store = new ConfigStore(root);
            store.SaveNotificationSettings(new NotificationSettings { Enabled = true, WebhookUrl = "https://discord.com/api/webhooks/1/abc", NotifyOnStopped = true });

            var loaded = store.LoadNotificationSettings();
            Assert.True(loaded.Enabled);
            Assert.Equal("https://discord.com/api/webhooks/1/abc", loaded.WebhookUrl);
            Assert.True(loaded.NotifyOnStopped);
        }
    }
}
