import json

with open(r'E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\i18n.js', 'r', encoding='utf-8') as f:
    i18n_js = f.read()

new_vi_keys = {
    "tab_run_label": "Home",
    "tab_run_shortLabel": "HOME",
    "tab_general_label": "Tinh chỉnh",
    "tab_general_shortLabel": "CHỈNH",
    "tab_logs_label": "Theo dõi",
    "tab_logs_shortLabel": "THEO DÕI",
    "wizard_title": "Cài đặt lần đầu",
    "wizard_subtitle": "Thiết lập kết nối với giả lập của bạn",
    "chon_gia_lap": "1. Chọn Giả Lập Đang Chạy",
    "kiem_tra_ket_noi": "2. Kiểm tra kết nối",
    "chay_kiem_tra": "Chạy Kiểm Tra",
    "hoan_tat": "Hoàn tất",
    "cau_hinh_farm": "Cấu hình Farm",
    "chon_chuc_nang": "Chọn chức năng:",
    "chi_can_chon_chuc_nang_bot_se_tu_dong": "Chỉ cần chọn chức năng, bot sẽ tự động áp dụng cấu hình tối ưu nhất.",
    "dieu_kien_dung": "Điều kiện dừng (Tùy chọn)",
    "cai_dat_co_ban": "Cài đặt cơ bản",
    "xin_quan_tu_dong": "Xin quân hội (tự động)",
    "hien_tuy_chon_nang_cao": "Hiện tùy chọn nâng cao",
    "cai_dat_khac": "Cài đặt khác",
    "lang_dem_tro_choi_hoi": "Làng Đêm & Trò Chơi Hội",
    "sap_ra_mat": "Sắp ra mắt",
    "ket_qua_phien": "Kết quả phiên chạy",
    "nhat_ky_ky_thuat": "Nhật ký kỹ thuật (Log)"
}

new_en_keys = {
    "tab_run_label": "Home",
    "tab_run_shortLabel": "HOME",
    "tab_general_label": "Settings",
    "tab_general_shortLabel": "SET",
    "tab_logs_label": "Monitor",
    "tab_logs_shortLabel": "MON",
    "wizard_title": "First-time Setup",
    "wizard_subtitle": "Configure connection to your emulator",
    "chon_gia_lap": "1. Select Running Emulator",
    "kiem_tra_ket_noi": "2. Check Connection",
    "chay_kiem_tra": "Run Validation",
    "hoan_tat": "Complete",
    "cau_hinh_farm": "Farming Profile",
    "chon_chuc_nang": "Select function:",
    "chi_can_chon_chuc_nang_bot_se_tu_dong": "Just select a function, the bot will automatically apply the optimal configuration.",
    "dieu_kien_dung": "Stop conditions (Optional)",
    "cai_dat_co_ban": "Basic Settings",
    "xin_quan_tu_dong": "Request Clan Troops (auto)",
    "hien_tuy_chon_nang_cao": "Show advanced options",
    "cai_dat_khac": "Other Settings",
    "lang_dem_tro_choi_hoi": "Builder Base & Clan Games",
    "sap_ra_mat": "Coming Soon",
    "ket_qua_phien": "Session Results",
    "nhat_ky_ky_thuat": "Technical Logs"
}

# we find the "vi: {" and inject the keys inside it
def inject_keys(content, lang, keys):
    start = content.find(f'{lang}: {{') + len(f'{lang}: {{')
    if start < len(f'{lang}: {{'):
        return content
    
    injected_str = "\\n".join([f'  "{k}": "{v}",' for k, v in keys.items()])
    return content[:start] + "\\n" + injected_str + content[start:]

i18n_js = inject_keys(i18n_js, 'vi', new_vi_keys)
i18n_js = inject_keys(i18n_js, 'en', new_en_keys)

with open(r'E:\Projects\CV-AUT\src\Simplimixi\Frontend\Web\i18n.js', 'w', encoding='utf-8') as f:
    f.write(i18n_js)
