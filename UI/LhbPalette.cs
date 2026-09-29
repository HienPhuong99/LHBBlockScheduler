using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using LHBBlockScheduler.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Palette "LHB Premium" (P11, lệnh LHBPALETTE): bảng nút lệnh dock cạnh màn hình AutoCAD như Properties,
    /// kèm trạng thái bản quyền / bộ block mẫu / số khu vực. Bật / tắt bằng lệnh LHBPALETTE.
    /// </summary>
    internal static class LhbPalette
    {
        private static PaletteSet _ps;
        private static PaletteControl _ctrl;

        public static void Toggle()
        {
            if (_ps == null)
            {
                _ps = new PaletteSet("LHB Premium", new Guid("8F2C1A5E-4B7D-4E21-9C3A-6D5B0E7F1A42"))
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton,
                    MinimumSize = new Size(230, 420),
                    DockEnabled = DockSides.Left | DockSides.Right
                };
                _ctrl = new PaletteControl();
                _ps.Add("Lệnh", _ctrl);
                _ps.Visible = true;
                Logger.Log("[Palette] Tạo palette LHB Premium");
            }
            else
            {
                _ps.Visible = !_ps.Visible;
            }
            if (_ps.Visible) _ctrl.RefreshStatus();
        }

        private class PaletteControl : UserControl
        {
            private readonly Label _status;

            public PaletteControl()
            {
                Font = new Font("Segoe UI", 9F);
                BackColor = SystemColors.Window;
                var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
                _status = new Label { Width = 210, Height = 64, ForeColor = Color.DimGray };
                flow.Controls.Add(_status);
                foreach (var (panel, buttons) in RibbonBuilder.Layout)
                {
                    flow.Controls.Add(new Label { Text = panel.ToUpper(), Width = 210, Height = 22, Font = new Font(Font, FontStyle.Bold), ForeColor = UiKit.Blue, Padding = new Padding(0, 6, 0, 0) });
                    foreach (var b in buttons)
                    {
                        var btn = new Button
                        {
                            Text = "  " + b.Text.Replace("\n", " "),
                            Width = 210,
                            Height = 34,
                            TextAlign = ContentAlignment.MiddleLeft,
                            ImageAlign = ContentAlignment.MiddleLeft,
                            TextImageRelation = TextImageRelation.ImageBeforeText,
                            Image = RibbonBuilder.Icon(b, 24),
                            FlatStyle = FlatStyle.Flat,
                            Tag = b.Command
                        };
                        btn.FlatAppearance.BorderColor = Color.Gainsboro;
                        new ToolTip().SetToolTip(btn, $"{b.Tip} (lệnh {b.Command})");
                        btn.Click += (s, e) => Run((string)((Button)s).Tag);
                        flow.Controls.Add(btn);
                    }
                }
                Controls.Add(flow);
            }

            public void RefreshStatus()
            {
                try
                {
                    var doc = AcApp.DocumentManager.MdiActiveDocument;
                    int zones = doc == null ? 0 : ZoneManager.LoadDefs(doc.Database).Count;
                    _status.Text = $"{LicenseManager.StatusText}\nBộ block mẫu: {SettingsManager.Current.CurrentTemplateSet}\nKhu vực trong bản vẽ: {zones}";
                }
                catch (Exception ex)
                {
                    _status.Text = "";
                    Logger.Warn($"[Palette] {ex.Message}");
                }
            }

            private void Run(string cmd)
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Logger.Log($"[Palette] Bấm nút {cmd}");
                doc.SendStringToExecute("\x03\x03" + cmd + " ", true, false, true);
                RefreshStatus();
            }
        }
    }
}
