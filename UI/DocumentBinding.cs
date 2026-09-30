using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using LHBBlockScheduler.Core;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Gắn form / hộp thoại modeless với bản vẽ của nó (v9.4, lỗi B1). Trước đây form giữ Document nhưng không theo dõi
    /// bản vẽ bị đóng / đổi: đóng bản vẽ rồi bấm nút trên form = dùng Document đã huỷ (nguy cơ crash AutoCAD); sang bản
    /// vẽ khác bấm "Xuất bảng" = hỏi điểm, ghi bảng vào bản vẽ không active.
    ///  - Bản vẽ của form bị đóng -> form tự đóng (DocumentClosing = true: form không hỏi gì thêm).
    ///  - Chuyển sang bản vẽ khác -> form ẩn; quay lại đúng bản vẽ -> form hiện lại.
    ///  - EnsureActive: nút thao tác trên bản vẽ kiểm tra lại trước khi chạy.
    /// </summary>
    internal sealed class DocumentBinding
    {
        private readonly Form _form;
        private readonly Document _doc;
        private readonly string _docName;
        private bool _hiddenBySwitch;
        private bool _closing;

        // Bản vẽ đang trong sự kiện đóng: form con (Show(owner)) bị đóng theo form cha TRƯỚC khi binding của chính nó
        // nhận sự kiện -> vẫn biết bản vẽ đang đóng để không hỏi / tự lưu
        private static readonly System.Collections.Generic.HashSet<Document> _closingDocs = new System.Collections.Generic.HashSet<Document>();

        /// <summary>Bản vẽ của form đang bị đóng (form đang tự đóng theo): không hỏi gì thêm, không thao tác bản vẽ.</summary>
        public bool DocumentClosing => _closing || _closingDocs.Contains(_doc);

        private DocumentBinding(Form form, Document doc)
        {
            _form = form;
            _doc = doc;
            _docName = SafeName(doc);
        }

        public static DocumentBinding Bind(Form form, Document doc)
        {
            if (form == null || doc == null) return null;
            var b = new DocumentBinding(form, doc);
            var dm = Application.DocumentManager;
            dm.DocumentToBeDestroyed += b.OnToBeDestroyed;
            dm.DocumentActivated += b.OnActivated;
            form.FormClosed += (s, e) =>
            {
                dm.DocumentToBeDestroyed -= b.OnToBeDestroyed;
                dm.DocumentActivated -= b.OnActivated;
            };
            Logger.Log($"[DocumentBinding] '{form.GetType().Name}' gắn với bản vẽ '{b._docName}'");
            return b;
        }

        /// <summary>Bản vẽ của form còn mở và đang là bản vẽ hiện hành.</summary>
        public bool IsActive
        {
            get
            {
                if (DocumentClosing) return false;
                try { return Application.DocumentManager.MdiActiveDocument == _doc; }
                catch { return false; }
            }
        }

        /// <summary>Kiểm tra trước thao tác trên bản vẽ: không đúng bản vẽ thì báo, trả false.</summary>
        public bool EnsureActive(IWin32Window owner)
        {
            if (IsActive) return true;
            Logger.Warn($"[DocumentBinding] '{_form.GetType().Name}': thao tác bị chặn vì bản vẽ hiện hành không phải '{_docName}'");
            MessageBox.Show(owner, DocumentClosing
                    ? $"Bản vẽ '{_docName}' đã đóng. Hãy quét lại (LHBSCAN) trên bản vẽ đang mở."
                    : $"Cửa sổ này thuộc bản vẽ '{_docName}'. Chuyển về bản vẽ đó rồi thao tác lại.",
                "LHB Block Scheduler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void OnToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != _doc) return;
            _closing = true;
            bool added = _closingDocs.Add(_doc);
            try
            {
                if (_form.IsDisposed) return;
                Logger.Log($"[DocumentBinding] Bản vẽ '{_docName}' đang đóng -> đóng '{_form.GetType().Name}'");
                _form.Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[DocumentBinding] Đóng '{_form.GetType().Name}' theo bản vẽ");
            }
            finally
            {
                if (added) _closingDocs.Remove(_doc);
            }
        }

        private void OnActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (_form.IsDisposed || DocumentClosing) return;
            try
            {
                if (e.Document == _doc)
                {
                    if (!_hiddenBySwitch) return;
                    _hiddenBySwitch = false;
                    _form.Show();
                    Logger.Log($"[DocumentBinding] Quay lại bản vẽ '{_docName}' -> hiện lại '{_form.GetType().Name}'");
                }
                else if (_form.Visible)
                {
                    _hiddenBySwitch = true;
                    _form.Hide();
                    Logger.Log($"[DocumentBinding] Chuyển sang bản vẽ '{SafeName(e.Document)}' -> ẩn '{_form.GetType().Name}' của bản vẽ '{_docName}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[DocumentBinding] Ẩn / hiện '{_form.GetType().Name}' khi đổi bản vẽ");
            }
        }

        private static string SafeName(Document doc)
        {
            try { return doc == null ? "(không có)" : Path.GetFileName(doc.Name); }
            catch { return "?"; }
        }
    }
}
