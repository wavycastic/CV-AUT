import re

with open(r'E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\app.js', 'r', encoding='utf-8') as f:
    js = f.read()

# Replace the tabs definition
old_tabs = """const tabs = [
  { id: "run", label: "Run", shortLabel: "RUN" },
  { id: "general", label: "Làng Chính", shortLabel: "CHÍNH" },
  { id: "night", label: "Làng Đêm", shortLabel: "ĐÊM" },
  { id: "clanGames", label: "Trò Chơi Hội", shortLabel: "HỘI" },
  { id: "config", label: "Cfg", shortLabel: "CFG" },
  { id: "save", label: "Lưu", shortLabel: "LƯU" },
  { id: "account", label: "Acc", shortLabel: "ACC" },
  { id: "stats", label: "Thống Kê", shortLabel: "T.KÊ" },
  { id: "logs", label: "Nhật Ký", shortLabel: "LOG" },
];"""

new_tabs = """const tabs = [
  { id: "run", label: "Home", shortLabel: "HOME" },
  { id: "general", label: "Tinh chỉnh", shortLabel: "CHỈNH" },
  { id: "logs", label: "Theo dõi", shortLabel: "THEO DÕI" },
];"""

js = js.replace(old_tabs, new_tabs)

# Inject safeGetElement
safe_get_element = """
const safeGetElement = (id) => {
  const el = document.getElementById(id);
  if (el) return el;
  const dummy = document.createElement("div");
  dummy.id = id;
  dummy.value = "";
  dummy.checked = false;
  return dummy;
};

const els = {"""

js = js.replace('const els = {', safe_get_element)
js = js.replace('document.getElementById(', 'safeGetElement(')
# we must fix up the document.querySelectorAll calls inside els if any?
# els uses document.querySelectorAll for clanTasks and advancedConfigControls
js = js.replace('Array.from(safeGetElement', 'Array.from(document.querySelectorAll')
# actually wait, let's just restore document.getElementById where it isn't part of els.
# It's better to just replace `document.getElementById` with `safeGetElement` globally for UI components, 
# but let's be more precise.
js = js.replace('safeGetElement(', 'document.getElementById(') # revert
js = js.replace('const els = {', 'const safeGetElement = (id) => { const el = document.getElementById(id); if (el) return el; const dummy = document.createElement("div"); dummy.id = id; dummy.value = ""; dummy.checked = false; return dummy; };\n\nconst els = {')

# Find the block: `const els = { ... };` and replace `document.getElementById` inside it.
match = re.search(r'const els = \{(.*?)\};', js, re.DOTALL)
if match:
    els_block = match.group(1)
    els_block = els_block.replace('document.getElementById', 'safeGetElement')
    js = js[:match.start(1)] + els_block + js[match.end(1):]


# Implement Wizard logic at the end of the file
wizard_logic = """
// Setup Wizard Logic
document.addEventListener("DOMContentLoaded", () => {
  const wizardCompleted = localStorage.getItem("wizardCompleted");
  const wizard = document.getElementById("setupWizard");
  
  if (!wizardCompleted && wizard) {
    wizard.classList.remove("hidden");
    wizard.classList.add("flex");
  }

  const validateBtn = document.getElementById("wizardValidateBtn");
  const continueBtn = document.getElementById("wizardContinueBtn");
  const resultDiv = document.getElementById("wizardValidationResult");

  if (validateBtn) {
    validateBtn.addEventListener("click", async () => {
      validateBtn.disabled = true;
      validateBtn.textContent = "Đang kiểm tra...";
      
      const emulator = document.getElementById("wizardEmulatorType")?.value || "BlueStacks";
      
      // Update the main form emulator value
      if (els.emulatorType) els.emulatorType.value = emulator;
      
      postMessage("validateSetup");
      
      // We will listen to the validation result in handleMessage
    });
  }

  if (continueBtn) {
    continueBtn.addEventListener("click", () => {
      localStorage.setItem("wizardCompleted", "true");
      wizard.classList.remove("flex");
      wizard.classList.add("hidden");
    });
  }
});

// Advanced Settings Logic
document.addEventListener("DOMContentLoaded", () => {
  const toggleBtn = document.getElementById("toggleAdvancedSettings");
  const content = document.getElementById("advancedSettingsContent");
  const icon = document.getElementById("advancedSettingsIcon");
  
  if (toggleBtn && content && icon) {
    toggleBtn.addEventListener("click", () => {
      const isHidden = content.classList.contains("hidden");
      if (isHidden) {
        content.classList.remove("hidden");
        content.classList.add("flex");
        icon.style.transform = "rotate(180deg)";
      } else {
        content.classList.add("hidden");
        content.classList.remove("flex");
        icon.style.transform = "rotate(0deg)";
      }
    });
  }

  const toggleLogsBtn = document.getElementById("toggleTechnicalLogs");
  const logsContent = document.getElementById("logsCard");
  const logsIcon = document.getElementById("technicalLogsIcon");
  
  if (toggleLogsBtn && logsContent && logsIcon) {
    toggleLogsBtn.addEventListener("click", () => {
      const isHidden = logsContent.classList.contains("hidden");
      if (isHidden) {
        logsContent.classList.remove("hidden");
        logsContent.classList.add("flex");
        logsIcon.style.transform = "rotate(180deg)";
      } else {
        logsContent.classList.add("hidden");
        logsContent.classList.remove("flex");
        logsIcon.style.transform = "rotate(0deg)";
      }
    });
  }

  // Handle Home Preset selection
  const homePresetSelect = document.getElementById("configPresetSelect");
  if (homePresetSelect) {
    // Fill it with backend presets
    homePresetSelect.addEventListener("change", (e) => {
      state.selectedConfigPresetId = state.configPresets.find(p => p.name === e.target.value)?.id;
      if (typeof applySelectedConfigPreset === "function") {
          applySelectedConfigPreset();
      }
    });
  }
});
"""

js += wizard_logic

# Fix renderConfigPresets to populate homePresetSelect
render_presets_hook = """
  if (els.configPresetTableBody) {
"""
new_render_presets = """
  const homePresetSelect = document.getElementById("configPresetSelect");
  if (homePresetSelect) {
    const currentVal = homePresetSelect.value;
    homePresetSelect.innerHTML = "";
    let farmSelected = false;
    state.configPresets.forEach((preset) => {
      const opt = document.createElement("option");
      opt.value = preset.name;
      opt.textContent = preset.name;
      if (preset.name === "Farm tài nguyên" || preset.name === "Farm tai nguyen") {
         opt.selected = true;
         farmSelected = true;
      }
      homePresetSelect.appendChild(opt);
    });
    if (!farmSelected && state.configPresets.length > 0) {
        homePresetSelect.value = state.configPresets[0].name;
    }
  }

  if (els.configPresetTableBody) {
"""
js = js.replace(render_presets_hook, new_render_presets)

# Handle validation message to update Wizard
handle_msg_hook = """case "validationResult":
      state.validationResults = payload.results;
      setValidationResults(payload.results);
      break;"""
new_handle_msg = """case "validationResult":
      state.validationResults = payload.results;
      setValidationResults(payload.results);
      
      // Update Wizard
      const resultDiv = document.getElementById("wizardValidationResult");
      const validateBtn = document.getElementById("wizardValidateBtn");
      const continueBtn = document.getElementById("wizardContinueBtn");
      
      if (resultDiv && validateBtn) {
         validateBtn.disabled = false;
         validateBtn.textContent = "Chạy Kiểm Tra Lại";
         resultDiv.classList.remove("hidden");
         
         const hasErrors = payload.results.some(r => !r.success && r.type === "error");
         
         if (hasErrors) {
            resultDiv.innerHTML = `<div class="text-red-500 font-bold text-sm">❌ Có lỗi xảy ra</div><div class="text-xs text-gray-300">Vui lòng kiểm tra lại độ phân giải, DPI và thiết lập giả lập. Mở CoC trước khi chạy.</div>`;
            if (continueBtn) continueBtn.classList.add("hidden");
         } else {
            resultDiv.innerHTML = `<div class="text-green-500 font-bold text-sm">✅ Thiết lập chuẩn</div><div class="text-xs text-gray-300">Bạn đã có thể bắt đầu sử dụng.</div>`;
            if (continueBtn) continueBtn.classList.remove("hidden");
         }
      }
      break;"""
js = js.replace(handle_msg_hook, new_handle_msg)

# Modify readConfigForm to hardcode `useDefaultConfig = true`
read_config_hook = """function readConfigForm() {"""
new_read_config = """function readConfigForm() {
  if (els.useDefaultConfig) els.useDefaultConfig.checked = true; // Hardcode
  if (els.requestTroops) els.requestTroops.checked = document.getElementById("requestTroopsCheckbox")?.checked || false;
"""
js = js.replace(read_config_hook, new_read_config)

with open(r'E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\app.js', 'w', encoding='utf-8') as f:
    f.write(js)
