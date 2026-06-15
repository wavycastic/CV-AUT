import re

with open(
    r"E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\index.html", "r", encoding="utf-8"
) as f:
    html = f.read()


# We want to delete: Night Tab, ClanGames Tab, Config Tab, Account Tab.
def remove_section(html_str, section_id):
    pattern = re.compile(
        rf'(<!--\s*Tab.*?-->\s*)?<section[^>]*id="{section_id}"[^>]*>.*?</section>',
        re.DOTALL,
    )
    return re.sub(pattern, "", html_str)


html = remove_section(html, "nightTab")
html = remove_section(html, "clanGamesTab")
html = remove_section(html, "configTab")
html = remove_section(html, "accountTab")
html = remove_section(html, "saveConfigTab")

# We need to redesign runTab to be Home
# It should contain: Preset selector, Stop condition
home_tab = """
                    <!-- Tab 1: Home -->
                    <section
                        id="runTab"
                        data-tab="run"
                        class="tab-panel flex-1 overflow-y-auto min-w-0 min-h-0"
                    >
                        <div class="content-stack pr-1">
                            <fieldset class="win-groupbox border-2 border-win-blue/50 p-3 mb-2 rounded-md shadow-sm bg-win-panel">
                                <legend class="win-legend text-sm font-bold text-win-blue px-2" data-i18n="cau_hinh_farm">Cấu hình Farm</legend>
                                <div class="flex flex-col gap-3 mt-2">
                                    <div class="flex items-center gap-3">
                                        <label class="win-label w-[120px] shrink-0 text-right font-bold text-xs" for="configPresetSelect" data-i18n="chon_chuc_nang">Chọn chức năng:</label>
                                        <select id="configPresetSelect" class="win-combobox h-[28px] text-xs flex-1 rounded border-win-border shadow-inner cursor-pointer font-semibold bg-win-input">
                                            <!-- Managed by app.js -->
                                        </select>
                                    </div>
                                    <p class="text-[11px] text-gray-400 ml-[132px] leading-relaxed" data-i18n="chi_can_chon_chuc_nang_bot_se_tu_dong">Chỉ cần chọn chức năng, bot sẽ tự động áp dụng cấu hình tối ưu nhất.</p>
                                </div>
                            </fieldset>

                            <fieldset class="win-groupbox p-3 rounded-md shadow-sm">
                                <legend class="win-legend text-sm font-bold px-2" data-i18n="dieu_kien_dung">Điều kiện dừng (Tùy chọn)</legend>
                                <div class="flex flex-col gap-3 mt-2">
                                    <div class="flex items-center gap-4">
                                        <label class="win-checkbox flex items-center gap-2 cursor-pointer">
                                            <input id="stopAfterBattlesEnabled" type="checkbox" class="win-checkbox-input w-4 h-4" />
                                            <span class="text-xs font-semibold" data-i18n="dung_sau_x_tran">Dừng sau X trận</span>
                                        </label>
                                        <input id="stopAfterBattles" type="number" min="0" class="win-textbox h-[28px] text-xs w-[80px] text-right rounded shadow-inner" />
                                    </div>
                                    <div class="flex items-center gap-4">
                                        <label class="win-checkbox flex items-center gap-2 cursor-pointer">
                                            <input id="stopAfterMinutesEnabled" type="checkbox" class="win-checkbox-input w-4 h-4" />
                                            <span class="text-xs font-semibold" data-i18n="dung_sau_x_phut">Dừng sau X phút</span>
                                        </label>
                                        <input id="stopAfterMinutes" type="number" min="0" class="win-textbox h-[28px] text-xs w-[80px] text-right rounded shadow-inner" />
                                    </div>
                                </div>
                            </fieldset>
                        </div>
                    </section>
"""
html = re.sub(
    r'<!-- Tab 1: Run.*?<section[^>]*id="runTab"[^>]*>.*?</section>',
    home_tab,
    html,
    flags=re.DOTALL,
)

# Add Setup Wizard at the end of body
wizard_html = """
        <!-- Setup Wizard -->
        <div id="setupWizard" class="fixed inset-0 z-[100] hidden flex-col items-center justify-center bg-black/80 backdrop-blur-sm p-4">
            <div class="bg-win-panel border-2 border-win-border rounded-lg shadow-2xl w-full max-w-md overflow-hidden flex flex-col">
                <div class="bg-win-blue text-white px-4 py-3 border-b border-win-border">
                    <h2 class="text-lg font-bold" data-i18n="wizard_title">Cài đặt lần đầu</h2>
                    <p class="text-xs opacity-80 mt-1" data-i18n="wizard_subtitle">Thiết lập kết nối với giả lập của bạn</p>
                </div>
                <div class="p-5 flex flex-col gap-4 bg-win-bg">
                    <div class="flex flex-col gap-2">
                        <label class="font-semibold text-sm" for="wizardEmulatorType" data-i18n="chon_gia_lap">1. Chọn Giả Lập Đang Chạy</label>
                        <select id="wizardEmulatorType" class="win-combobox h-[32px] text-sm w-full cursor-pointer rounded border-win-border">
                            <option value="BlueStacks">BlueStacks</option>
                            <option value="MEmu">MEmu</option>
                            <option value="Nox">Nox</option>
                            <option value="LDPlayer">LDPlayer</option>
                            <option value="MuMu">MuMu</option>
                        </select>
                    </div>

                    <div class="flex flex-col gap-2">
                        <label class="font-semibold text-sm" data-i18n="kiem_tra_ket_noi">2. Kiểm tra kết nối</label>
                        <button id="wizardValidateBtn" type="button" class="action-button bg-win-blue hover:bg-win-blue/80 text-white h-[36px] font-bold text-sm rounded shadow">
                            <span data-i18n="chay_kiem_tra">Chạy Kiểm Tra</span>
                        </button>
                    </div>

                    <div id="wizardValidationResult" class="hidden flex flex-col gap-2 p-3 border rounded">
                        <!-- Validation messages will be injected here -->
                    </div>
                </div>
                <div class="p-3 border-t border-win-border bg-win-panel flex justify-end">
                    <button id="wizardContinueBtn" type="button" class="action-button bg-success h-[32px] px-6 font-bold text-sm hidden rounded shadow">
                        <span data-i18n="hoan_tat">Hoàn tất</span>
                    </button>
                </div>
            </div>
        </div>
"""
html = html.replace("</body>", wizard_html + "\n    </body>")

# In General Tab (Tinh chỉnh), we want to wrap some items in "Nâng cao".
# But actually it's easier to just hide them using CSS class 'hidden' or re-write the tab.
# Let's replace generalTab entirely.
general_tab = """
                    <!-- Tab 2: Settings (Tinh chỉnh) -->
                    <section
                        id="generalTab"
                        data-tab="general"
                        class="tab-panel hidden flex-1 overflow-y-auto min-w-0 min-h-0"
                    >
                        <div class="content-stack pr-1">
                            <!-- Basic Settings -->
                            <fieldset class="win-groupbox p-3 mb-2 rounded-md shadow-sm">
                                <legend class="win-legend text-sm font-bold px-2" data-i18n="cai_dat_co_ban">Cài đặt cơ bản</legend>
                                <div class="flex flex-col gap-3 mt-1">
                                    <div class="flex items-center gap-3">
                                        <img src="https://appassets-resources/Templates/icon_attack.png" class="w-4 h-4 shrink-0" alt="Chiến thuật" />
                                        <label class="win-label w-[90px] shrink-0 font-bold text-xs" for="strategy" data-i18n="chien_thuat">Chiến thuật:</label>
                                        <select id="strategy" class="win-combobox h-[28px] text-xs flex-1 rounded border-win-border"></select>
                                    </div>

                                    <div class="grid grid-cols-3 gap-2 border border-win-border/50 bg-win-bg/50 p-2 rounded">
                                        <div class="flex items-center gap-1.5">
                                            <img src="https://appassets-resources/Templates/icon_gold.png" class="w-4 h-4 shrink-0" />
                                            <label class="win-label shrink-0 font-semibold text-[11px]" for="goldThreshold" data-i18n="vang">Vàng:</label>
                                            <input id="goldThreshold" type="number" min="0" class="win-textbox h-[24px] text-[11px] w-full text-right" />
                                        </div>
                                        <div class="flex items-center gap-1.5">
                                            <img src="https://appassets-resources/Templates/icon_elixir.png" class="w-4 h-4 shrink-0" />
                                            <label class="win-label shrink-0 font-semibold text-[11px]" for="elixirThreshold" data-i18n="dau_hong">Dầu:</label>
                                            <input id="elixirThreshold" type="number" min="0" class="win-textbox h-[24px] text-[11px] w-full text-right" />
                                        </div>
                                        <div class="flex items-center gap-1.5">
                                            <img src="https://appassets-resources/Templates/icon_de.png" class="w-4 h-4 shrink-0" />
                                            <label class="win-label shrink-0 font-semibold text-[11px]" for="darkThreshold" data-i18n="dau_den">Đen:</label>
                                            <input id="darkThreshold" type="number" min="0" class="win-textbox h-[24px] text-[11px] w-full text-right" />
                                        </div>
                                    </div>

                                    <div class="flex items-center justify-between gap-2 mt-1">
                                        <label class="win-checkbox flex items-center gap-2 cursor-pointer">
                                            <input id="upgradeWall" type="checkbox" class="win-checkbox-input w-4 h-4" />
                                            <span class="font-bold text-xs" data-i18n="kich_hoat_nang_cap_tuong">Tự động nâng tường</span>
                                        </label>
                                        <div class="flex items-center gap-2">
                                            <label class="win-label shrink-0 font-semibold text-[11px]" for="wallLevel" data-i18n="cap">Cấp:</label>
                                            <select id="wallLevel" class="wall-control win-combobox h-[24px] text-[11px] w-[60px]"></select>
                                        </div>
                                    </div>

                                    <div class="flex items-center justify-between gap-2 border-t border-win-border/30 pt-2 mt-1">
                                        <label class="win-checkbox flex items-center gap-2 cursor-pointer">
                                            <input id="requestTroopsCheckbox" type="checkbox" class="win-checkbox-input w-4 h-4" />
                                            <span class="font-bold text-xs" data-i18n="xin_quan_tu_dong">Xin quân hội (tự động)</span>
                                        </label>
                                    </div>
                                </div>
                            </fieldset>

                            <!-- Advanced Settings Toggle -->
                            <button id="toggleAdvancedSettings" type="button" class="w-full flex items-center justify-between bg-win-panel border border-win-border p-2 rounded hover:bg-win-bg transition-colors cursor-pointer text-left">
                                <span class="font-bold text-xs text-gray-300" data-i18n="hien_tuy_chon_nang_cao">Hiện tùy chọn nâng cao</span>
                                <span id="advancedSettingsIcon" class="text-xs transition-transform">▼</span>
                            </button>

                            <!-- Advanced Settings Content -->
                            <div id="advancedSettingsContent" class="hidden flex-col gap-2 mt-2">
                                <!-- ADB / Emulator Hidden Configs -->
                                <fieldset class="win-groupbox p-2">
                                    <legend class="win-legend text-xs" data-i18n="thiet_bi_gia_lap">Thiết bị / Giả lập</legend>
                                    <div class="flex items-center gap-2 mb-2">
                                        <label class="win-label w-[70px] text-xs" for="deviceSelect">Device:</label>
                                        <select id="deviceSelect" class="win-combobox h-[24px] text-xs flex-1"></select>
                                        <button id="refreshDevicesButton" type="button" class="action-button h-[24px] px-2 text-xs">Refresh</button>
                                    </div>
                                    <div class="flex items-center gap-2">
                                        <label class="win-label w-[70px] text-xs" for="adbHost">ADB IP:</label>
                                        <input id="adbHost" type="text" class="win-textbox h-[24px] text-xs flex-1" />
                                        <label class="win-label text-xs" for="adbPort">Port:</label>
                                        <input id="adbPort" type="number" class="win-textbox h-[24px] text-xs w-[60px]" />
                                    </div>
                                </fieldset>

                                <!-- Other General Configs -->
                                <fieldset class="win-groupbox p-2">
                                    <legend class="win-legend text-xs" data-i18n="cai_dat_khac">Cài đặt khác</legend>
                                    <div class="grid grid-cols-2 gap-2">
                                        <div class="flex items-center gap-2">
                                            <label class="win-label text-xs" for="totalResourceThreshold">Tổng Vàng+Dầu:</label>
                                            <input id="totalResourceThreshold" type="number" class="win-textbox h-[24px] text-xs w-[70px]" />
                                        </div>
                                        <div class="flex items-center gap-2">
                                            <label class="win-label text-xs" for="targetLogic">Logic:</label>
                                            <select id="targetLogic" class="win-combobox h-[24px] text-xs flex-1">
                                                <option value="total">Tổng + Dầu đen</option>
                                                <option value="or">Một ngưỡng bất kỳ</option>
                                                <option value="and">Tất cả ngưỡng</option>
                                            </select>
                                        </div>
                                        <div class="flex items-center gap-2">
                                            <label class="win-label text-xs" for="attackMode">Chế độ:</label>
                                            <select id="attackMode" class="win-combobox h-[24px] text-xs flex-1">
                                                <option value="attack">Tấn công</option>
                                                <option value="donate_only">Chỉ Donate</option>
                                            </select>
                                        </div>
                                    </div>
                                    <div class="flex flex-col gap-1 mt-2 border-t border-win-border/20 pt-2">
                                        <label class="win-checkbox">
                                            <input id="useCake" type="checkbox" class="win-checkbox-input" />
                                            <span class="text-xs">Sử dụng Bánh kem hội thành</span>
                                        </label>
                                        <label class="win-checkbox">
                                            <input id="useEventTroops" type="checkbox" class="win-checkbox-input" />
                                            <span class="text-xs">Sử dụng lính sự kiện</span>
                                        </label>
                                        <label class="win-checkbox">
                                            <input id="smartSurrenderEnabled" type="checkbox" class="win-checkbox-input" />
                                            <span class="text-xs">Đầu hàng thông minh</span>
                                        </label>
                                    </div>
                                </fieldset>

                                <fieldset class="win-groupbox p-2 opacity-50 relative">
                                    <div class="absolute inset-0 z-10 bg-win-bg/30 flex items-center justify-center cursor-not-allowed">
                                        <span class="bg-win-panel border border-win-border px-3 py-1 font-bold text-xs" data-i18n="sap_ra_mat">Sắp ra mắt</span>
                                    </div>
                                    <legend class="win-legend text-xs" data-i18n="lang_dem_tro_choi_hoi">Làng Đêm & Trò Chơi Hội</legend>
                                    <div class="flex flex-col gap-2">
                                        <label class="win-checkbox"><input type="checkbox" class="win-checkbox-input" disabled /> <span class="text-xs">Bật Làng Đêm</span></label>
                                        <label class="win-checkbox"><input type="checkbox" class="win-checkbox-input" disabled /> <span class="text-xs">Nhận Trò Chơi Hội</span></label>
                                    </div>
                                </fieldset>

                                <div class="hidden">
                                    <input id="villageSelect" />
                                    <input id="trainModeSmart" name="trainMode" type="radio" value="smart" />
                                    <input id="trainModeQuick" name="trainMode" type="radio" value="quick" />
                                    <input id="quickSlot" />
                                    <input id="surrenderAfterSecondsEnabled" type="checkbox" />
                                    <input id="surrenderAfterSeconds" type="number" />
                                    <input id="surrenderLowResourcesEnabled" type="checkbox" />
                                    <input id="surrenderLowResourcesThreshold" type="number" />
                                    <input id="wallGoldThreshold" type="number" />
                                    <input id="wallElixirThreshold" type="number" />
                                    <input id="emulatorType" />
                                    <!-- Add hidden config tab fields to prevent null error if referenced by ID in HTML. App.js may check existence. -->
                                </div>
                            </div>
                        </div>
                    </section>
"""
html = re.sub(
    r'<!-- Tab 2: General.*?<section[^>]*id="generalTab"[^>]*>.*?</section>',
    general_tab,
    html,
    flags=re.DOTALL,
)

# Modify Logs to Theo Dõi by adding stats into it.
logs_tab = """
                    <!-- Tab 3: Logs (Theo dõi) -->
                    <section
                        id="logsTab"
                        data-tab="logs"
                        class="tab-panel hidden flex-1 overflow-y-auto min-w-0 min-h-0"
                    >
                        <div class="content-stack pr-1">
                            <!-- Friendly Status Banner -->
                            <div id="monitorStatusBanner" class="p-3 mb-2 rounded-md border flex items-center gap-3 shadow-sm bg-win-panel border-win-border">
                                <div id="monitorStatusIcon" class="text-2xl">⏳</div>
                                <div class="flex-1">
                                    <h3 id="monitorStatusTitle" class="font-bold text-sm text-win-text">Sẵn sàng chạy</h3>
                                    <p id="monitorStatusDesc" class="text-xs text-gray-400 mt-0.5">Vui lòng chọn chức năng và bấm Bắt đầu</p>
                                </div>
                            </div>

                            <!-- Simplified Stats -->
                            <fieldset class="win-groupbox p-3 mb-2 rounded-md shadow-sm">
                                <legend class="win-legend text-sm font-bold px-2" data-i18n="ket_qua_phien">Kết quả phiên chạy</legend>
                                <div class="grid grid-cols-2 gap-3 mt-1">
                                    <div class="flex items-center gap-2">
                                        <img src="https://appassets-resources/Templates/icon_gold.png" class="w-5 h-5" />
                                        <div class="flex flex-col">
                                            <span class="text-[10px] text-gray-400 font-semibold uppercase" data-i18n="vang">Vàng</span>
                                            <span id="goldValue" class="text-yellow-400 font-bold text-sm">0</span>
                                        </div>
                                    </div>
                                    <div class="flex items-center gap-2">
                                        <img src="https://appassets-resources/Templates/icon_elixir.png" class="w-5 h-5" />
                                        <div class="flex flex-col">
                                            <span class="text-[10px] text-gray-400 font-semibold uppercase" data-i18n="dau_hong">Dầu hồng</span>
                                            <span id="elixirValue" class="text-fuchsia-400 font-bold text-sm">0</span>
                                        </div>
                                    </div>
                                    <div class="flex items-center gap-2">
                                        <img src="https://appassets-resources/Templates/icon_de.png" class="w-5 h-5" />
                                        <div class="flex flex-col">
                                            <span class="text-[10px] text-gray-400 font-semibold uppercase" data-i18n="dau_den">Dầu đen</span>
                                            <span id="darkValue" class="text-cyan-400 font-bold text-sm">0</span>
                                        </div>
                                    </div>
                                    <div class="flex items-center gap-2">
                                        <img src="https://appassets-resources/Templates/icon_attack.png" class="w-5 h-5" />
                                        <div class="flex flex-col">
                                            <span class="text-[10px] text-gray-400 font-semibold uppercase" data-i18n="tran_danh">Trận đánh</span>
                                            <span id="attacksValue" class="text-win-text font-bold text-sm">0</span>
                                        </div>
                                    </div>
                                </div>
                            </fieldset>

                            <!-- Technical Logs Toggle -->
                            <button id="toggleTechnicalLogs" type="button" class="w-full flex items-center justify-between bg-win-panel border border-win-border p-2 rounded hover:bg-win-bg transition-colors cursor-pointer text-left mb-2">
                                <span class="font-bold text-xs text-gray-300" data-i18n="nhat_ky_ky_thuat">Nhật ký kỹ thuật (Log)</span>
                                <span id="technicalLogsIcon" class="text-xs transition-transform">▼</span>
                            </button>

                            <fieldset
                                id="logsCard"
                                class="hidden win-groupbox flex-col gap-1.5 p-2"
                            >
                                <div class="flex items-center justify-between gap-2">
                                    <div id="logsFilters" class="flex flex-wrap gap-1">
                                        <button type="button" class="filter-chip filter-chip-active" data-log-filter="all">Tất cả</button>
                                        <button type="button" class="filter-chip filter-chip-inactive" data-log-filter="bot">Bot</button>
                                        <button type="button" class="filter-chip filter-chip-inactive" data-log-filter="adb">ADB</button>
                                    </div>
                                    <button id="clearLogsButton" class="action-button h-[22px] px-2.5 py-0" data-i18n="xoa">Xóa</button>
                                </div>
                                <pre id="logsOutput" class="portrait-log-output w-full min-w-0 overflow-x-hidden overflow-y-auto whitespace-pre-wrap break-all border border-win-border bg-black p-1.5 font-mono text-[10px] leading-tight text-emerald-400 h-[200px]"></pre>
                                <p id="logsEmptyState" class="text-[10px] text-gray-400 text-center py-4 hidden" data-i18n="khong_co_nhat_ky_nao">Không có nhật ký nào.</p>
                            </fieldset>

                            <!-- Hidden Stats Panel elements so JS doesn't crash -->
                            <div class="hidden">
                                <span id="starsValue"></span>
                                <span id="goldPerHourValue"></span>
                                <span id="elixirPerHourValue"></span>
                                <span id="darkPerHourValue"></span>
                                <span id="wallsUpgradedValue"></span>
                                <span id="clanTasksValue"></span>
                                <span id="clanPointsValue"></span>
                                <div id="historyTableBody"></div>
                            </div>
                        </div>
                    </section>
"""
html = re.sub(
    r'<!-- Tab 4: Logs.*?<section[^>]*id="logsTab"[^>]*>.*?</section>',
    logs_tab,
    html,
    flags=re.DOTALL,
)

# Delete statsTab section
html = remove_section(html, "statsTab")

# Enhance bottom action bar style
action_bar_old = 'class="grid grid-cols-3 gap-1.5 mt-0.5"'
action_bar_new = 'class="grid grid-cols-3 gap-2 mt-1 px-1 py-2"'
html = html.replace(action_bar_old, action_bar_new)

html = html.replace(
    'id="startButton"\n                                class="action-button bg-success"',
    'id="startButton"\n                                class="action-button bg-success text-sm font-bold h-[36px]"',
)
html = html.replace(
    'id="pauseButton"\n                                class="action-button bg-warning"',
    'id="pauseButton"\n                                class="action-button bg-warning text-sm font-bold h-[36px]"',
)
html = html.replace(
    'id="stopButton"\n                                class="action-button bg-danger"',
    'id="stopButton"\n                                class="action-button bg-danger text-sm font-bold h-[36px]"',
)


with open(
    r"E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\index.html", "w", encoding="utf-8"
) as f:
    f.write(html)
