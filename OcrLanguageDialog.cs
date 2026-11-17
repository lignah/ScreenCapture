namespace ScreenCapture;

/// <summary>Add (download) or delete PaddleOCR recognition languages.</summary>
internal sealed class OcrLanguageDialog : Form
{
    private readonly bool _add;
    private readonly AppSettings _settings;
    private readonly CheckedListBox _list;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _ok;
    private readonly Button _cancel;
    private CancellationTokenSource? _cts;
    private readonly List<OcrLanguageInfo> _items = new();

    public OcrLanguageDialog(bool add, AppSettings settings)
    {
        _add = add;
        _settings = settings;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = add ? "OCR 언어 추가" : "OCR 언어 삭제";
        Icon = TrayContext.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(520, 400);
        Font = new Font("Malgun Gothic", 9f);

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0),
            Text = add
                ? "다운로드할 언어를 선택하세요 (PaddleOCR 공식 모델, RapidOCR 저장소, SHA-256 검증).\n저장 위치: " + OcrLanguages.UserDir
                : "삭제할 언어를 선택하세요. 사용 중인 언어와 기본 포함 언어는 삭제할 수 없어요.",
        };
        _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 16, Visible = false };
        _status = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 4, 8, 0) };
        _ok = new Button { Text = add ? "다운로드" : "삭제", AutoSize = true };
        _cancel = new Button { Text = "닫기", AutoSize = true };
        _ok.Click += async (_, _) => await OnOk();
        _cancel.Click += (_, _) => { if (_cts != null) _cts.Cancel(); else Close(); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        body.Controls.Add(_list);
        Controls.Add(body);
        Controls.Add(head);
        Controls.Add(_status);
        Controls.Add(_progress);
        Controls.Add(buttons);
        _list.ItemCheck += OnItemCheck;
        Fill();
    }

    private void Fill()
    {
        _list.Items.Clear();
        _items.Clear();
        var installed = OcrLanguages.Installed();
        if (_add)
        {
            foreach (var info in OcrLanguages.Catalog.Where(c => installed.All(i => i.Info.Id != c.Id)))
            {
                _items.Add(info);
                _list.Items.Add($"{info.Name} · {info.Version} · {info.RecSize / 1048576.0:0.0} MB");
            }
            _status.Text = _items.Count == 0 ? "모든 언어가 이미 설치되어 있어요." : "";
        }
        else
        {
            foreach (var l in installed.Where(l => !l.Bundled))
            {
                _items.Add(l.Info);
                bool active = string.Equals(l.Info.Id, _settings.OcrLanguage, StringComparison.OrdinalIgnoreCase);
                _list.Items.Add(l.Info.Name + (active ? "  (사용 중)" : ""));
            }
            _status.Text = _items.Count == 0 ? "삭제할 수 있는 다운로드 언어가 없어요." : "";
        }
        _ok.Enabled = _items.Count > 0;
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_add || e.NewValue != CheckState.Checked) return;
        var info = _items[e.Index];
        if (string.Equals(info.Id, _settings.OcrLanguage, StringComparison.OrdinalIgnoreCase))
        {
            e.NewValue = CheckState.Unchecked;
            _status.Text = "사용 중인 언어예요. 트레이 메뉴에서 다른 언어로 바꾼 뒤 삭제하세요.";
        }
    }

    private async Task OnOk()
    {
        var chosen = _list.CheckedIndices.Cast<int>().Select(i => _items[i]).ToList();
        if (chosen.Count == 0) { _status.Text = "언어를 선택하세요."; return; }
        if (!_add)
        {
            if (OcrLanguages.Installed().Count - chosen.Count < 1) { _status.Text = "마지막 언어는 삭제할 수 없어요."; return; }
            foreach (var c in chosen)
            {
                try { OcrLanguages.Delete(c.Id); }
                catch (Exception ex) { ErrorLog.Write(ex); _status.Text = $"{c.Name} 삭제 실패: {ex.Message}"; return; }
            }
            Fill();
            _status.Text = $"{chosen.Count}개 언어를 삭제했어요.";
            return;
        }

        _ok.Enabled = false;
        _list.Enabled = false;
        _cancel.Text = "취소";
        _progress.Visible = true;
        _cts = new CancellationTokenSource();
        int ok = 0;
        try
        {
            foreach (var c in chosen)
            {
                var name = c.Name;
                var progress = new Progress<(long Done, long Total)>(p =>
                {
                    int pct = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
                    _progress.Value = pct;
                    _status.Text = $"{name} 다운로드 중… {p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB";
                });
                await OcrLanguages.DownloadAsync(c, progress, _cts.Token);
                ok++;
            }
            _status.Text = $"{ok}개 언어를 추가했어요. 트레이 메뉴 → OCR 설정 → OCR 언어에서 선택하세요.";
        }
        catch (OperationCanceledException) { _status.Text = "취소했어요."; }
        catch (Exception ex) { ErrorLog.Write(ex); _status.Text = "다운로드 실패: " + ex.Message; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _progress.Visible = false;
            _cancel.Text = "닫기";
            _list.Enabled = true;
            Fill();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); e.Cancel = true; }
        base.OnFormClosing(e);
    }
}
