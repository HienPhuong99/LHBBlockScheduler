;;; ==========================================================================
;;; LHB.lsp - Auto-loader cho tiện ích LHBBlockScheduler (AutoCAD Add-in)
;;;
;;; File LISP này mang MD5 của đúng LHBBlockScheduler.dll đóng gói cùng nó (build.ps1 ghi vào
;;; *LHB_EXPECTED_MD5*). Chỉ NETLOAD thư mục có build-info.txt cùng MD5, tìm theo thứ tự:
;;;   1. (findfile "LHB.lsp") - thư mục add-in nằm trong Support File Search Path
;;;   2. Thư mục đã nhớ trong registry HKCU\Software\LHBBlockScheduler\InstallDir
;;;   3. Quét Downloads / Desktop / Documents (sâu 3 cấp) - nơi hay giải nén file zip
;;;   4. Hộp thoại chọn file LHBBlockScheduler.dll
;;; Lỗi cũ (test 29/09/2026): registry còn nhớ thư mục bản CŨ -> kéo thả LHB.lsp bản mới vẫn NETLOAD
;;; bản cũ, tắt mở lại CAD cũng vậy. Nay thư mục có MD5 khác bị bỏ qua.
;;; Sau NETLOAD hỏi DLL (hàm lhb-dllinfo) xem AutoCAD thật sự đang chạy bản nào: .NET không gỡ được DLL
;;; đã nạp, nếu bản cũ đã nạp trước trong phiên (vd do Startup Suite) thì báo tắt hẳn AutoCAD.
;;; KHÔNG nạp LHBLoader.dll: loader chỉ dùng trên máy build (đọc %APPDATA%\...\Runtime).
;;; v9.7: mỗi dòng AutoCAD 1 bản DLL, chọn theo ACADVER:
;;;   24.x (AutoCAD 2021 - 2024, .NET Framework 4.8) -> <thư mục add-in>\LHBBlockScheduler.dll
;;;   25.x (AutoCAD 2025 - 2026, .NET 8)             -> <thư mục add-in>\net8\LHBBlockScheduler.dll
;;;   26.x trở lên (AutoCAD 2027, .NET 10)           -> <thư mục add-in>\net10\LHBBlockScheduler.dll
;;; Thư mục add-in vẫn nhận ra bằng build-info.txt + MD5 bản 2021 - 2024 ở thư mục gốc; bản net8 / net10 kiểm thêm
;;; build-info.txt trong thư mục con của nó.
;;; ==========================================================================

(vl-load-com)

(setq *LHB_REG_KEY* "HKEY_CURRENT_USER\\Software\\LHBBlockScheduler")

;; MD5 của DLL đi kèm. Còn nguyên chữ mẫu (chạy thẳng từ thư mục build) = không kiểm tra MD5.
;; *LHB_EXPECTED_MD5* = bản AutoCAD 2021 - 2024 ở thư mục gốc, cũng là "dấu" nhận ra đúng thư mục add-in.
(setq *LHB_EXPECTED_MD5* "@@LHB_BUILD_MD5@@")
;; v9.7: bản AutoCAD 2025 - 2026 (thư mục con net8) và AutoCAD 2027 (thư mục con net10).
;; Còn chữ mẫu trong khi *LHB_EXPECTED_MD5* đã có MD5 = gói này không có bản cho dòng AutoCAD đó.
(setq *LHB_EXPECTED_MD5_NET8* "@@LHB_BUILD_MD5_NET8@@")
(setq *LHB_EXPECTED_MD5_NET10* "@@LHB_BUILD_MD5_NET10@@")

;; Phiên bản add-in đi kèm (build.ps1 ghi từ DLL, v9.4). Còn chữ mẫu = chạy thẳng từ thư mục build.
(setq *LHB_VERSION* "@@LHB_VERSION@@")

(defun c:LHBHELP ()
  (princ "\n==================================================================")
  (princ (strcat "\n  LHBBlockScheduler - DANH SÁCH LỆNH KHẢ DỤNG (bản cho " (lhb:TargetName) "):"))
  (princ "\n  - LHBSCAN       : Mở giao diện Thống kê Block đầy đủ tính năng")
  (princ "\n  - LHBMAU        : Thư viện block mẫu (thêm / xoá / sắp xếp / chèn block mẫu)")
  (princ "\n  - LHBLENH       : Bảng danh sách lệnh, chạy lệnh, đổi phím tắt (mặc định gõ LHB)")
  (princ "\n  - LHBKHOPCOT    : Khớp độ rộng cột bảng đã xuất theo chữ (như Excel)")
  (princ (strcat "\n  ---- PREMIUM" (if (vl-string-search "LHB_VERSION" *LHB_VERSION*) "" (strcat " (" *LHB_VERSION* ")")) " ----"))
  (princ "\n  - LHBKHUVUC     : Tầng / khu vực -> bảng có cột SL từng khu")
  (princ "\n  - LHBCAPNHAT    : Cập nhật bảng đã xuất sau khi sửa bản vẽ")
  (princ "\n  - LHBNHIEUBV    : Thống kê nhiều bản vẽ DWG cùng lúc")
  (princ "\n  - LHBCHIEUDAI   : Chiều dài ống / dây theo layer")
  (princ "\n  - LHBSOATLOI    : Soát lỗi đếm (explode, đổi tên, layer tắt, trùng)")
  (princ "\n  - LHBDANHSO     : Đánh số thiết bị tự động")
  (princ "\n  - LHBVUNGBV     : Vẽ vùng bảo vệ đầu báo / đầu phun")
  (princ "\n  - LHBTHAYBLOCK  : Thay block hàng loạt")
  (princ "\n  - LHBMAUBANG    : Mẫu bảng xuất (tiêu đề, dòng tổng, màu)")
  (princ "\n  - LHBPALETTE    : Bảng công cụ LHB dock cạnh màn hình")
  (princ "\n  - LHBRIBBON     : Bật / tắt tab Ribbon LHB Premium")
  (princ "\n  - LHBBANQUYEN   : Mã máy, kích hoạt bản quyền Premium")
  (princ "\n  - LHBLEGEND     : Quét bảng Legend (Table) có sẵn vào bộ block mẫu")
  (princ "\n  - LHBDUPCLEAR   : Xoá vòng đỏ / đường dẫn đánh dấu block trùng")
  (princ "\n  - LHBCLEARCACHE : Xoá bộ nhớ đệm hình ảnh thumbnail")
  (princ "\n  - LHBLOG        : Mở file nhật ký ghi lỗi (log.txt)")
  (princ "\n  - LHBLOGPATH    : In đường dẫn tuyệt đối của file log (log.txt)")
  (princ "\n  - LHBDIAG       : Thu thập toàn bộ thông tin chẩn đoán hệ thống ra file text")
  (princ "\n  - LHBVERSION    : Xem thông tin phiên bản, cấu hình, build time, MD5")
  (princ "\n  - LHBSETPATH    : Chọn lại thư mục chứa LHBBlockScheduler.dll")
  (princ "\n==================================================================\n")
  (princ)
)

;; T nếu LISP này có MD5 thật (đã đóng gói bằng build.ps1)
(defun lhb:Md5Check-p ()
  (not (vl-string-search "LHB_BUILD_MD5" *LHB_EXPECTED_MD5*))
)

;; v9.7: số phiên bản AutoCAD đang chạy (ACADVER "25.0s (LMS Tech)" -> 25.0)
(defun lhb:AcadVer ()
  (atof (getvar "ACADVER"))
)

;; v9.7: thư mục con chứa DLL cho AutoCAD đang chạy: "" = thư mục gốc (2021 - 2024), "net8" (2025 - 2026),
;; "net10" (2027; AutoCAD mới hơn cũng thử bản .NET 10, add-in tự báo "chưa kiểm")
(defun lhb:TargetSub ( / v)
  (setq v (lhb:AcadVer))
  (cond ((< v 25.0) "")
        ((< v 26.0) "net8")
        (t "net10"))
)

;; Tên dòng AutoCAD của bản DLL cần nạp (để báo trên dòng lệnh)
(defun lhb:TargetName ( / sub)
  (setq sub (lhb:TargetSub))
  (cond ((= sub "") "AutoCAD 2021 - 2024")
        ((= sub "net8") "AutoCAD 2025 - 2026")
        (t "AutoCAD 2027"))
)

;; MD5 cần có của DLL cho AutoCAD đang chạy
(defun lhb:TargetMd5 ( / sub)
  (setq sub (lhb:TargetSub))
  (cond ((= sub "") *LHB_EXPECTED_MD5*)
        ((= sub "net8") *LHB_EXPECTED_MD5_NET8*)
        (t *LHB_EXPECTED_MD5_NET10*))
)

;; T nếu gói này (đã đóng gói) KHÔNG có bản cho dòng AutoCAD đang chạy (MD5 còn chữ mẫu)
(defun lhb:TargetMissing-p ()
  (and (lhb:Md5Check-p)
       (vl-string-search "LHB_BUILD_MD5" (lhb:TargetMd5)))
)

;; Thư mục chứa DLL cần nạp, trong thư mục gốc add-in
(defun lhb:TargetDir (root / sub)
  (setq sub (lhb:TargetSub))
  (if (= sub "") root (strcat root "\\" sub))
)

;; Trả về thư mục (không có "\" cuối) nếu trong đó có LHBBlockScheduler.dll, ngược lại nil
(defun lhb:ValidDir (dir)
  (if (and dir (/= dir "") (findfile (strcat dir "\\LHBBlockScheduler.dll")))
    dir
  )
)

;; Đọc dòng "MD5: ..." trong build-info.txt cạnh DLL (viết hoa), nil nếu không có
(defun lhb:ReadBuildMd5 (dir / f line pos md5)
  (if (setq f (open (strcat dir "\\build-info.txt") "r"))
    (progn
      (while (and (not md5) (setq line (read-line f)))
        (if (setq pos (vl-string-search "MD5:" line))
          (setq md5 (strcase (vl-string-trim " \t\r\n" (substr line (+ pos 5)))))
        )
      )
      (close f)
    )
  )
  md5
)

;; T nếu thư mục có DLL đúng bản của LISP này (hoặc LISP chưa có MD5 thì chỉ cần có DLL)
(defun lhb:DirMatches (dir)
  (and (lhb:ValidDir dir)
       (or (not (lhb:Md5Check-p))
           (equal (lhb:ReadBuildMd5 dir) *LHB_EXPECTED_MD5*)))
)

;; Quét thư mục con (sâu tối đa depth cấp) tìm thư mục có DLL đúng MD5
(setq *LHB_SCAN_COUNT* 0)
(defun lhb:ScanDir (dir depth / found)
  (setq *LHB_SCAN_COUNT* (1+ *LHB_SCAN_COUNT*))
  (cond
    ((lhb:DirMatches dir) dir)
    ((and (> depth 0) (< *LHB_SCAN_COUNT* 5000))
     (foreach sub (vl-directory-files dir nil -1)
       (if (and (not found) (not (member sub '("." ".."))))
         (setq found (lhb:ScanDir (strcat dir "\\" sub) (1- depth)))
       )
     )
     found)
  )
)

(defun lhb:ScanUserFolders ( / home found)
  (setq home (getenv "USERPROFILE") *LHB_SCAN_COUNT* 0)
  (if home
    (foreach sub '("Downloads" "Desktop" "Documents")
      (if (and (not found) (vl-file-directory-p (strcat home "\\" sub)))
        (setq found (lhb:ScanDir (strcat home "\\" sub) 3))
      )
    )
  )
  found
)

;; v9.7: chọn DLL trong thư mục con net8 / net10 -> dùng thư mục gốc (thư mục cha) nếu đó là thư mục add-in
(defun lhb:RootOf (dir / parent)
  (if (and dir
           (member (strcase (vl-filename-base dir)) '("NET8" "NET10"))
           (setq parent (vl-filename-directory dir))
           (lhb:ValidDir parent))
    parent
    dir
  )
)

;; Hộp thoại chọn DLL, nhớ thư mục vào registry
(defun lhb:AskDir ( / lastDir picked dir)
  (setq lastDir (vl-registry-read *LHB_REG_KEY* "InstallDir"))
  (setq picked
    (getfiled "Chọn file LHBBlockScheduler.dll trong thư mục vừa giải nén"
              (if (lhb:ValidDir lastDir) (strcat lastDir "\\") "")
              "dll" 0))
  (if (and picked (setq dir (lhb:ValidDir (lhb:RootOf (vl-filename-directory picked)))))
    (progn
      (vl-registry-write *LHB_REG_KEY* "InstallDir" dir)
      (if (and (lhb:Md5Check-p) (not (lhb:DirMatches dir)))
        (princ (strcat "\n[LHB CẢNH BÁO] DLL vừa chọn KHÁC bản của file LHB.lsp này (MD5 "
                       (vl-princ-to-string (lhb:ReadBuildMd5 dir)) " / cần " *LHB_EXPECTED_MD5* ")."))
      )
      dir
    )
  )
)

(defun lhb:FindDir ( / lspFile dir regDir)
  (setq regDir (vl-registry-read *LHB_REG_KEY* "InstallDir"))
  (cond
    ;; 1. Support Path
    ((and (setq lspFile (findfile "LHB.lsp"))
          (setq dir (vl-filename-directory lspFile))
          (lhb:DirMatches dir))
     (princ (strcat "\n[LHB] Tìm thấy add-in qua Support Path: " dir))
     dir)
    ;; 2. Registry (chỉ dùng nếu đúng bản)
    ((lhb:DirMatches regDir)
     (princ (strcat "\n[LHB] Dùng thư mục đã nhớ: " regDir))
     regDir)
    ;; 3. Quét thư mục hay giải nén
    ((and (lhb:Md5Check-p)
          (progn
            (if (lhb:ValidDir regDir)
              (princ (strcat "\n[LHB] Thư mục đã nhớ là BẢN KHÁC (bỏ qua): " regDir)))
            (princ "\n[LHB] Đang tìm thư mục add-in đúng bản trong Downloads / Desktop / Documents...")
            (setq dir (lhb:ScanUserFolders))))
     (vl-registry-write *LHB_REG_KEY* "InstallDir" dir)
     (princ (strcat "\n[LHB] Tìm thấy: " dir))
     dir)
    ;; 4. Hỏi người dùng
    (t
     (princ "\n[LHB] Chưa tìm thấy thư mục add-in đúng bản -> hãy chọn file LHBBlockScheduler.dll trong thư mục vừa giải nén...")
     (lhb:AskDir))
  )
)

;; ("đường dẫn DLL" "MD5") của bản AutoCAD đang chạy; nil nếu chưa nạp hoặc bản cũ (chưa có hàm lhb-dllinfo).
;; Phải kiểm (type lhb-dllinfo) trước: gọi (vl-catch-all-apply 'lhb-dllinfo nil) khi hàm chưa có thì AutoCAD
;; báo "; error: bad function: LHB-DLLINFO" NGOÀI vùng bắt lỗi và dừng cả file LISP (test 29/09/2026 v5).
(defun lhb:LoadedInfo ( / r)
  (if (type lhb-dllinfo)
    (progn
      (setq r (vl-catch-all-apply '(lambda () (lhb-dllinfo)) nil))
      (if (or (vl-catch-all-error-p r) (not (listp r))) nil r)
    )
  )
)

;; T nếu bản đang chạy đúng bản của LISP này (v9.7: đúng bản cho dòng AutoCAD đang chạy)
(defun lhb:LoadedMatches (info)
  (and info
       (or (not (lhb:Md5Check-p))
           (= (strcase (cadr info)) (lhb:TargetMd5))))
)

(defun lhb:WarnOldLoaded (info)
  (princ "\n******************************************************************")
  (princ "\n[LHB CẢNH BÁO] AutoCAD ĐANG CHẠY BẢN CŨ của add-in, không phải bản đi kèm file LHB.lsp này.")
  (if info
    (princ (strcat "\n  Bản đang chạy: " (car info) "  (MD5 " (cadr info) ")"))
    (progn
      (princ "\n  Không thấy bản mới chạy sau NETLOAD. Có 2 khả năng:")
      (princ "\n   a) NETLOAD bị huỷ / lỗi (xem dòng lệnh phía trên, vd bấm Do Not Load ở hộp thoại bảo mật).")
      (princ "\n   b) Bản cũ (trước 29/09/2026) đã nạp trước đó trong phiên này.")
    )
  )
  ;; v9.7: đang chạy đúng gói nhưng nhầm bản (DLL 2021 - 2024 ở thư mục gốc nạp vào AutoCAD 2025+, thường do NETLOAD tay)
  (if (and info (/= (lhb:TargetSub) "") (lhb:Md5Check-p) (= (strcase (cadr info)) *LHB_EXPECTED_MD5*))
    (princ (strcat "\n  (Đó là bản cho AutoCAD 2021 - 2024 ở thư mục gốc, nạp nhầm. AutoCAD này cần DLL trong thư mục "
                   (lhb:TargetSub) ")"))
  )
  (princ (strcat "\n  Bản cần dùng : MD5 " (lhb:TargetMd5) " (bản cho " (lhb:TargetName) ")"))
  (princ "\n  AutoCAD không gỡ được DLL đã nạp. Cách xử lý:")
  (princ "\n   1. Gõ APPLOAD > nút Contents (Startup Suite): xoá LHB.lsp cũ nếu có.")
  (princ "\n   2. Tắt HẲN AutoCAD, mở lại, rồi kéo thả LHB.lsp của bản mới.")
  (princ "\n******************************************************************\n")
)

;; dir = thư mục gốc add-in. v9.7: nạp DLL của dòng AutoCAD đang chạy (thư mục gốc / net8 / net10)
(defun lhb:NetloadDir (dir / tdir dll info)
  (setq tdir (lhb:TargetDir dir)
        dll  (strcat tdir "\\LHBBlockScheduler.dll"))
  (cond
    ((not (findfile dll))
     (princ (strcat "\n[LHB LỖI] Không có bản cho " (lhb:TargetName) ": thiếu file " dll))
     (princ "\n  Giải nén lại ĐỦ file zip (giữ nguyên các thư mục net8, net10 bên trong) rồi kéo thả LHB.lsp.\n"))
    ;; Bản net8 / net10: build-info.txt trong thư mục con phải đúng MD5 (bản gốc đã kiểm lúc tìm thư mục)
    ((and (lhb:Md5Check-p)
          (/= tdir dir)
          (not (equal (lhb:ReadBuildMd5 tdir) (lhb:TargetMd5))))
     (princ (strcat "\n[LHB LỖI] DLL trong " tdir " KHÁC bản của file LHB.lsp này (MD5 "
                    (vl-princ-to-string (lhb:ReadBuildMd5 tdir)) " / cần " (lhb:TargetMd5) ")."))
     (princ "\n  Giải nén lại file zip vào thư mục mới rồi kéo thả LHB.lsp trong thư mục đó.\n"))
    (t
     (setvar "CMDECHO" 0)
     (vl-cmdf "_.NETLOAD" dll)
     (setvar "CMDECHO" 1)
     (setq info (lhb:LoadedInfo))
     (cond
       ((not (lhb:LoadedMatches info))
        (lhb:WarnOldLoaded info))
       (t
        (setq *LHB_PLUGIN_LOADED* T)
        (princ (strcat "\n[LHB] Đã nạp bản cho " (lhb:TargetName) ": " (car info)))
        (princ (strcat "\n[LHB] MD5: " (cadr info) (if (lhb:Md5Check-p) " (đúng bản)" "")))
        (c:LHBHELP))
     ))
  )
)

(defun lhb:LoadPlugin ( / dir info)
  (setq info (lhb:LoadedInfo))
  (cond
    ;; v9.7: AutoCAD 2020 trở về trước (ACADVER < 24) không chạy được bản nào
    ((< (lhb:AcadVer) 24.0)
     (princ (strcat "\n[LHB LỖI] AutoCAD này (ACADVER " (getvar "ACADVER") ") chưa được hỗ trợ: cần AutoCAD 2021 trở lên.\n")))
    ;; v9.7: gói thiếu bản cho dòng AutoCAD này (vd build trên máy chưa có .NET 10 SDK chỉ có bản 2021 - 2024)
    ((lhb:TargetMissing-p)
     (princ (strcat "\n[LHB LỖI] Gói add-in này không có bản cho " (lhb:TargetName)
                    " (ACADVER " (getvar "ACADVER") "). Cần gói có thư mục " (lhb:TargetSub) ".\n")))
    ;; Đã nạp đúng bản trong phiên này
    ((lhb:LoadedMatches info)
     (setq *LHB_PLUGIN_LOADED* T)
     (princ (strcat "\n[LHB] Add-in đã nạp trước đó (đúng bản): " (car info)))
     (princ "\nGõ LHBSCAN để mở bảng thống kê, hoặc LHBHELP để xem trợ giúp.\n"))
    ;; Đã nạp bản khác (có hàm lhb-dllinfo nhưng MD5 khác)
    (info
     (lhb:WarnOldLoaded info))
    ;; Chưa nạp (hoặc bản rất cũ không có lhb-dllinfo: NETLOAD xong sẽ phát hiện)
    ((progn
       (princ "\n[LHB] Chưa có add-in trong phiên này -> tìm thư mục chứa DLL đúng bản...")
       (setq dir (lhb:FindDir)))
     (lhb:NetloadDir dir))
    (t
     (princ "\n[LHB LỖI] Không xác định được thư mục chứa LHBBlockScheduler.dll. Kéo thả lại LHB.lsp hoặc gõ LHBSETPATH.\n"))
  )
  (princ)
)

;; Chọn lại thư mục DLL. Lưu ý: AutoCAD không gỡ được DLL .NET đã nạp,
;; muốn dùng bản khác phải tắt/mở lại AutoCAD rồi kéo thả LHB.lsp.
(defun c:LHBSETPATH ( / dir)
  (if (setq dir (lhb:AskDir))
    (progn
      (princ (strcat "\n[LHB] Đã nhớ thư mục: " dir))
      (if (lhb:LoadedInfo)
        (princ "\n[LHB] Add-in đã nạp trong phiên này. Tắt và mở lại AutoCAD để dùng bản ở thư mục mới.")
        (lhb:NetloadDir dir)
      )
    )
    (princ "\n[LHB] Đã huỷ chọn thư mục.")
  )
  (princ)
)

;; Tự động chạy khi file LISP được nạp
(lhb:LoadPlugin)
