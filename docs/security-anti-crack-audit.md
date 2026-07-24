# Bao cao bao mat chong crack va dich nguoc

Ngay danh gia: 2026-07-14

## Tom tat

Repo co bien phap lam tang chi phi dich nguoc, chu yeu bang Native AOT, trimming va loai bo symbol. Tuy nhien, ban phat hanh hien tai chua co license enforcement va chua co trust boundary mat ma du manh de chong sua binary.

Danh gia tong quan:

| Hang muc | Diem | Ket luan |
| --- | ---: | --- |
| Chong dich nguoc | 6/10 | Native AOT lam kho phan tich hon managed IL, nhung van co the dich nguoc native code |
| Chong sua binary/tamper | 2/10 | Manifest SHA-256 khong duoc ky va co the bi tao lai |
| License/activation | 0/10 | License gate chua duoc trien khai |
| Bao ve asset | 2/10 | Asset dung XOR voi khoa tinh, chi mang tinh obfuscation |
| Chong debug | 3/10 | Co kiem tra co ban, nhung co bypass va chua chay dinh ky |
| Phat hanh/update | 2/10 | Artifact va update metadata chua duoc ky |

Khong co giai phap client-side nao chong crack tuyet doi. Muc tieu hop ly la tang chi phi tan cong, phat hien tamper va dua quyen cap phep ve trust boundary phia server.

## Pham vi

Danh gia tinh cac khu vuc sau:

- Cau hinh Native AOT, trimming va symbol stripping.
- License va activation gate.
- Runtime integrity verification.
- Anti-debugging.
- Bao ve template asset.
- Dong goi va code signing.
- Update metadata va kenh phan phoi.
- Secret duoc luu trong cau hinh nguoi dung.

GitNexus index khong co PDG/taint layer tai thoi diem danh gia. Vi vay, bao cao khong khang dinh repo khong co injection hoac data-flow vulnerability ngoai cac luong da duoc kiem tra tinh.

## Phat hien Critical

### SEC-001: License enforcement chua ton tai

Muc do: Critical

`src/frontend/ViewModels/LicenseViewModel.cs:5-12` xac nhan trang license moi la skeleton va license gate du kien o phase sau. Khong tim thay activation, entitlement, machine binding, expiry, revocation, signed license token hoac gate truoc automation.

Tac dong:

- Ban sao ung dung co the chay ma khong can license.
- Khong co server-side decision de thu hoi hoac gioi han quyen su dung.
- Them giao dien license ma khong gate tat ca automation entry point se khong ngan bypass.

Khuyen nghi:

- Dung license token duoc server ky bat doi xung.
- App chi chua public key de xac minh token; private key khong duoc nam trong client hoac repo.
- Gate tai tat ca entry point khoi dong automation, khong chi gate trong UI.
- Token nen co license ID, product, feature, expiry va device binding neu business yeu cau.
- Revocation va online validation can duoc quyet dinh theo kha nang hoat dong offline.

### SEC-002: Integrity manifest khong co chu ky

Muc do: Critical

`eng/scripts/write-integrity-manifest.ps1:20-27` tao SHA-256 cho tung file. Manifest duoc ghi canh ung dung tai `eng/scripts/write-integrity-manifest.ps1:55-61`. Runtime doc truc tiep hash trong manifest tai `src/backend/Core/ReleaseSecurity.cs:86-96` va so sanh tai `src/backend/Core/ReleaseSecurity.cs:163-174`.

Manifest khong co digital signature, HMAC voi khoa ngoai package, public-key verification hoac Authenticode trust validation.

Tac dong:

- Attacker sua EXE, DLL hoac template roi tinh lai manifest.
- SHA-256 hien tai chi phat hien corruption hoac sua file ma khong cap nhat manifest.

Khuyen nghi:

- Ky manifest bang private release key duoc bao ve ngoai repo.
- Nhung public key trong app va xac minh chu ky truoc khi tin bat ky path/hash nao.
- Tach key dung cho manifest khoi code-signing key neu quy trinh van hanh yeu cau.
- Bao ve va audit release pipeline, vi pipeline bi chiem quyen van co the phat hanh artifact hop le.

### SEC-003: Integrity policy co nhanh fail-open

Muc do: Critical

`src/backend/Core/ReleaseSecurity.cs:70-84` cho phep startup tiep tuc khi manifest khong ton tai va khong tim thay `.dat`. `src/backend/Core/TemplateAssetLoader.cs:13-29` va `:53-54` van chap nhan PNG thong thuong.

Tac dong:

- Co the xoa manifest va `.dat`, sau do dua PNG thuong vao package.
- Release khong bat buoc protected package layout tai runtime.

Khuyen nghi:

- Release build phai fail-closed neu manifest thieu, sai chu ky hoac thieu file bat buoc.
- Cam plaintext template fallback trong Release.
- Neu can fallback cho development, rang buoc bang compile-time Debug condition, khong dung environment variable runtime.

## Phat hien High

### SEC-004: Template encryption la XOR voi khoa tinh

Muc do: High

`eng/scripts/build-installer.ps1:315-359` tao key tu cac hang byte co dinh va XOR du lieu voi key lap. Native decoder tuong ung nam trong `src/native/simplimixi_native.c`, va export duoc goi boi `src/backend/Core/NativeTemplateCodec.cs:38-44`.

Tac dong:

- Key co the duoc trich tu script, DLL hoac runtime memory.
- Export decoder co the duoc goi truc tiep de giai ma asset.
- Khong co nonce va authentication tag, nen khong phat hien sua ciphertext mot cach mat ma.

Khuyen nghi:

- Xem co che hien tai la obfuscation, khong goi la encryption security boundary.
- Neu asset secrecy co gia tri kinh doanh, can dua xu ly nhay cam ve server khi kha thi.
- Neu van phai ship asset, authenticated encryption chi ngan sua doi va tang chi phi; khong the giu khoa bi mat tuyet doi tren client.

### SEC-005: Anti-debug co bypass trong release

Muc do: High

`src/backend/Core/ReleaseSecurity.cs:24-26` dinh nghia `AUTOCLASHOFCLAN20206_ALLOW_DEBUGGER`. `src/backend/Core/ReleaseSecurity.cs:126-129` bo qua debugger checks khi bien nay bang `1`.

Tac dong:

- Nguoi dung co the vo hieu hoa anti-debug ma khong can patch binary.
- Ten bien co the bi tim trong binary.

Khuyen nghi:

- Loai bypass khoi production build.
- Neu can chan doan production, dung artifact rieng duoc ky va khong phan phoi cong khai.
- Anti-debug chi la defense-in-depth, khong thay the license va signed integrity.

### SEC-006: Runtime policy khong duoc goi dinh ky

Muc do: High

`src/backend/Core/ReleaseSecurity.cs:43-57` dinh nghia `EnforceRuntimePolicy()` voi khoang kiem tra 15 giay. Static source review khong tim thay call site. Integrity startup duoc goi truoc UI tai `src/frontend/Program.cs:15-26`.

Tac dong:

- Anti-debug runtime du kien khong hoat dong.
- Integrity khong duoc kiem tra lai sau startup.
- Patch in-memory hoac thay file sau startup khong bi policy hien tai phat hien.

Khuyen nghi:

- Goi runtime policy tai boundary co gia tri, vi du truoc khi bat dau automation.
- Khong them polling day dac neu khong co threat model ro rang; polling de bi patch va tang complexity.
- Uu tien signed license, server checks va signed release truoc anti-debug nang.

### SEC-007: Artifact phat hanh chua duoc Authenticode signing

Muc do: High

`eng/scripts/build-installer.ps1:443-459` build installer nhung khong co buoc signing. `eng/installer/SimpliMixi.iss:8-27` khong cau hinh `SignTool` hoac signed uninstaller.

Tai thoi diem audit, cac artifact sau tra ve `NotSigned`:

- `publish/SimpliMixi-v0.6.2-Setup.exe`
- `publish/SimpliMixi-v0.6.2/SimpliMixi.exe`
- `publish/SimpliMixi-v0.6.2/simplimixi_native.dll`

Tac dong:

- Windows va nguoi dung khong xac minh duoc publisher.
- Package bi thay the truoc lan chay dau khong co dau hieu trust ro rang.
- SmartScreen reputation va incident attribution yeu hon.

Khuyen nghi:

- Ky EXE, native DLL va installer bang Authenticode certificate.
- Dung trusted timestamp server.
- Build phai fail neu signature thieu, sai signer hoac khong co timestamp.
- Private signing key nen nam trong HSM, managed signing service hoac secret store cua CI.

### SEC-008: Update metadata khong co chu ky va package hash

Muc do: High neu updater duoc trien khai; hien tai chua thay updater client hoat dong.

`eng/scripts/deploy-cloudflare.ps1:31-48` tao va upload update metadata cung installer. `eng/deploy/update.json` chi co version, URL va update policy; khong co SHA-256 hoac chu ky.

Tac dong:

- Neu updater duoc noi theo schema hien tai, compromise storage, Pages, DNS hoac deployment account co the phan phoi executable tuy y.
- HTTPS khong thay the artifact signature.

Khuyen nghi:

- Ky update metadata bang offline release key.
- Bao gom package SHA-256 va kich thuoc trong signed metadata.
- Xac minh Authenticode signer va package hash truoc execution.
- Khong cho force-update field co hieu luc truoc khi metadata signature hop le.

## Phat hien Medium

### SEC-009: Path containment dung string prefix

Muc do: Medium

`src/backend/Core/ReleaseSecurity.cs:146-150` dung `StartsWith(baseDirectory)` de kiem tra path. Prefix string khong dam bao path nam trong dung directory boundary; vi du sibling directory co cung prefix co the match.

Khuyen nghi:

- Canonicalize base path voi separator cuoi.
- Dung `Path.GetRelativePath` va tu choi rooted path cung path bat dau bang `..` segment.
- Chi xu ly path sau khi manifest signature da hop le.

### SEC-010: Startup integrity result duoc cache

Muc do: Medium

`src/backend/Core/ReleaseSecurity.cs:65-68` va `:98` dung `_startupValidated`, khien cac lan goi sau khong hash lai file.

Tac dong:

- File bi thay sau lan validation dau tien khong bi phat hien boi cung method.

Khuyen nghi:

- Xac minh lai tai security-sensitive boundary neu threat model bao gom post-start replacement.
- Tranh polling toan bo package lien tuc; chi kiem tra file sap dung va cac automation entry point quan trong.

### SEC-011: Discord webhook duoc luu plaintext

Muc do: Medium

`src/frontend/ConfigStore.cs:164-175` va `:189-200` doc/ghi notification settings trong JSON. Discord webhook URL chua credential nhung khong duoc bao ve bang DPAPI hoac Windows Credential Manager.

Tac dong:

- Malware hoac tai khoan co quyen doc profile co the lay webhook va gui message trai phep.

Khuyen nghi:

- Luu secret bang Windows Credential Manager hoac DPAPI theo current user.
- Khong ghi full webhook vao log.
- Ho tro revoke/replace webhook khi nghi ro ri.

### SEC-012: Asset decoder chap nhan plaintext

Muc do: Medium

`src/backend/Core/TemplateAssetLoader.cs:94-99` tra du lieu nguyen ban khi khong co expected magic header.

Tac dong:

- Format enforcement fail-open.
- File khong duoc ma hoa van co the duoc xu ly.

Khuyen nghi:

- Release phai tu choi asset khong dung protected format.
- Development fallback chi nen duoc compile trong Debug.

## Phat hien Low

### SEC-013: Native hardening chua duoc verify trong pipeline

Muc do: Low

`eng/scripts/build-native.ps1` build helper voi toi uu hoa, nhung pipeline khong thay buoc verify PE properties nhu ASLR, DEP/NX, CFG, stack protection hoac export minimization.

Khuyen nghi:

- Them release check cho PE security properties neu native helper tiep tuc chua logic co gia tri.
- Chi export cac function bat buoc.

### SEC-014: Native AOT van lo metadata, strings va imports

Muc do: Low

`eng/scripts/build-installer.ps1:218-230` da ghi nhan type names va native export van hien dien trong binary.

Tac dong:

- Native AOT lam phan tich kho hon nhung khong ngan static analysis.
- Algorithm va control flow quan trong van co the duoc tim va patch.

Khuyen nghi:

- Khong dua secret hoac trust decision chi vao viec code da duoc AOT.
- Chuyen quyet dinh license co gia tri sang server.

## Kiem soat dang co

### Native AOT

- `src/frontend/Simplimixi.csproj:22-36` bat `PublishAot`, full trimming va strip symbols.
- `src/backend/Simplimixi.Backend.csproj:10-15` bat AOT analysis cho backend.

Protected release khong ship managed IL, nen kho decompile hon managed DLL thong thuong.

### Loai bo debug va development artifacts

- `src/frontend/Simplimixi.csproj:50-54` loai Avalonia diagnostics khoi Release.
- `eng/scripts/build-installer.ps1:240-297` cam source, scripts, raw templates, PDB va XML trong protected package.

### Sensitive string gate

`eng/scripts/build-installer.ps1:200-238` quet mot so runtime va algorithm strings khoi binary. Day la release hygiene huu ich, nhung khong thay the obfuscation hoac native-code protection.

### Startup checks

`src/backend/Core/ReleaseSecurity.cs:102-123` kiem tra managed debugger, local debugger va remote debugger. `src/frontend/Program.cs:15-26` chay startup security policy truoc UI.

### Secret trong deployment

`eng/scripts/deploy-cloudflare.ps1:26-29` lay Cloudflare token tu environment. Khong phat hien production cloud credential, private key hoac API token duoc hard-code trong cac file da quet.

## Lo trinh khac phuc

### P0: Trust boundary co ban

1. Trien khai signed license token va gate moi automation entry point.
2. Ky Authenticode EXE, DLL va installer kem timestamp.
3. Ky integrity manifest bang asymmetric release key.
4. Release fail-closed khi manifest hoac protected asset khong hop le.

### P1: Phat hanh va secret

1. Ky update metadata va xac minh package hash cung Authenticode signer.
2. Bao ve Discord webhook bang DPAPI hoac Credential Manager.
3. Loai anti-debug environment bypass khoi production.

### P2: Defense-in-depth

1. Goi runtime policy tai automation boundary co gia tri.
2. Sua path containment va integrity cache policy.
3. Verify native PE hardening trong release pipeline.
4. Danh gia lai asset secrecy; chuyen asset/logic co gia tri cao ve server neu kha thi.

## Tieu chi chap nhan de xuat

- Release khong khoi dong neu manifest thieu, sai chu ky hoac co file sai hash.
- Sua manifest va tinh lai SHA-256 khong giup package gia vuot qua validation.
- Automation khong the bat dau khi license token thieu, het han, sai product hoac sai chu ky.
- Private license/release signing key khong nam trong source, package hoac CI log.
- EXE, DLL va installer co Authenticode signature hop le va timestamp.
- Updater tu choi metadata sai chu ky, package sai hash va signer khong dung.
- Release tu choi plaintext template.
- Debugger bypass khong ton tai trong production artifact.
- Webhook khong nam plaintext trong profile config hoac log.

## Gioi han danh gia

- Day la static review cua source, build scripts, installer config va artifact hien co.
- Chua thuc hien dynamic debugging, binary patching, fuzzing, runtime hooking hoac penetration test tren may ao.
- Chua co PDG/taint index, nen khong bao phu day du cac data-flow vulnerability.
- Diem so phan anh trang thai repo tai ngay danh gia, khong phai chung nhan bao mat.
