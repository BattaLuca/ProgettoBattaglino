// <intero file aggiornato — solo il metodo StartPlayback è stato modificato, il resto è identico>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ProgettoBattaglino
{
    // ==========================================
    // CLASSE PER IL SALVATAGGIO DEI PROGETTI
    // ==========================================
    public class AudioClipSaveData
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public float StartSec { get; set; }
        public float DurationSec { get; set; }
        public int LaneIndex { get; set; }
        public float Volume { get; set; }
        public bool IsLoopClip { get; set; }
        public float LoopLengthSec { get; set; }
        public List<SequencerNote> PianoNotes { get; set; }
        public int TotalSteps { get; set; }
        public float StepLengthSec { get; set; }
        public bool IsDrumKit { get; set; }
    }

    // ==========================================
    // CLASSE AudioClipInfo (a livello di namespace)
    // ==========================================
    public class AudioClipInfo
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public float StartSec { get; set; }
        public float DurationSec { get; set; }
        public int LaneIndex { get; set; }
        public float Volume { get; set; } = 1f;
        public List<float> Peaks { get; set; } = new List<float>();
        public bool PeaksReady { get; set; } = false;

        public List<NAudio.Wave.AudioFileReader> ActiveReaders { get; set; } = new List<NAudio.Wave.AudioFileReader>();

        public bool IsLoopClip { get; set; } = false;
        public float LoopLengthSec { get; set; } = 0f;

        public List<SequencerNote> PianoNotes { get; set; }
        public int TotalSteps { get; set; }
        public float StepLengthSec { get; set; } = 0f;

        public bool IsDrumKit { get; set; } = false;
    }

    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint,
                true);
            UpdateStyles();
        }
    }

    public partial class Form1 : Form
    {
        // =========================
        //  COSTANTI / PERCORSI
        // =========================
        private const string ImportFolderPath = @"C:\Users\Luca\Desktop\scuola\PROGETTO\ProgettoBattaglino\ProgettoBattaglino\Registrazioni";
        private static readonly string StrumentiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Strumenti");
        private static readonly string ProgettiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "progetti");

        private const int MixerSampleRate = 44100;
        private const int MixerChannels = 2;

        private static readonly string[] NoteNames = new[] { "C5", "B4", "A#4", "A4", "G#4", "G4", "F#4", "F4", "E4", "D#4", "D4", "C#4", "C4", "B3", "A#3", "A3", "G#3", "G3", "F#3", "F3", "E3", "D#3", "D3", "C#3", "C3" };
        private static readonly string[] DrumPieces = new[] { "Crash", "Ride", "HiHat", "Tom", "Snare", "Kick" };

        // =========================
        //  LAYOUT UI (DAW)
        // =========================
        private MenuStrip menuStripMain;
        private ToolStripMenuItem menuFile;
        private ToolStripMenuItem menuImporta;
        private ToolStripMenuItem menuAggiungi;

        private Panel pnlTopBar;
        private Panel pnlCenter;
        private Panel pnlBottomMixer;

        private BufferedPanel pnlRuler;
        private BufferedPanel pnlTrackSurface;

        private VScrollBar vScroll;
        private HScrollBar hScroll;
        private FlowLayoutPanel pnlMixerTracks;

        private int pixelsPerSecond = 70;
        private int timelineOffsetX = 0;
        private int rowHeight = 70;
        private const int HeaderW = 170;
        private const int TrackTopPadding = 60;
        private float timelineSeconds = 10f;

        private float playheadSec = 0f;
        private bool showPlayhead = false;

        private Button btnPlay;
        private Button btnStop;
        private Button btnRec;

        // =========================
        //  NAudio: RECORD / PLAYBACK
        // =========================
        private bool isRecording = false;
        private WaveInEvent waveSource;
        private WaveFileWriter waveFile;
        private string lastRecord;

        private WaveOutEvent outputDevice;
        private Timer timerPlay;
        private DateTime playbackStartUtc;
        private float playbackStartSec;

        // =========================
        //  DRAG CLIP
        // =========================
        private bool isDraggingClip = false;
        private int draggingClipIndex = -1;
        private int dragStartMouseX = 0;
        private float dragStartClipSec = 0f;

        private bool isDraggingRightEdge = false;
        private float dragStartDurationSec = 0f;

        // =========================
        //  CLIP / TRACK INFO
        // =========================
        // AudioClipInfo è definita a livello di namespace (vedi sotto)

        private readonly List<AudioClipInfo> clips = new List<AudioClipInfo>();
        private int selectedClipIndex = -1;
        private List<int> selectedClipIndices = new List<int>();
        private List<AudioClipInfo> clipClipboard = null;
        private bool cutPending = false;

        public Form1()
        {
            InitializeComponent();
            BuildUI();
            LayoutAll();
        }

        private void BuildUI()
        {
            Text = "ProgettoBattaglino - DAW";
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(30, 33, 38);
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            BuildMenu();

            pnlTopBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(45, 49, 58) };
            Controls.Add(pnlTopBar);
            BuildTopBar();

            pnlBottomMixer = new Panel { Dock = DockStyle.Bottom, Height = 220, BackColor = Color.FromArgb(35, 38, 46) };
            Controls.Add(pnlBottomMixer);
            BuildMixer();

            pnlCenter = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 30, 36) };
            Controls.Add(pnlCenter);

            hScroll = new HScrollBar { Dock = DockStyle.Bottom, Height = 16 };
            hScroll.Scroll += (s, e) => { timelineOffsetX = hScroll.Value; pnlRuler.Invalidate(); pnlTrackSurface.Invalidate(); };
            pnlCenter.Controls.Add(hScroll);

            pnlRuler = new BufferedPanel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(38, 41, 50) };
            pnlRuler.Paint += PnlRuler_Paint;
            pnlRuler.MouseDown += Timeline_MouseDownSeek;
            pnlCenter.Controls.Add(pnlRuler);

            pnlTrackSurface = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 26, 31), TabStop = true };
            pnlTrackSurface.Paint += PnlTrackSurface_Paint;
            pnlTrackSurface.MouseDown += Timeline_MouseDownSeek;
            pnlTrackSurface.MouseMove += PnlTrackSurface_MouseMove;
            pnlTrackSurface.MouseUp += PnlTrackSurface_MouseUp;
            pnlTrackSurface.ContextMenuStrip = CreateClipContextMenu();
            pnlCenter.Controls.Add(pnlTrackSurface);

            vScroll = new VScrollBar { Dock = DockStyle.Right, Width = 16 };
            vScroll.Scroll += (s, e) => pnlTrackSurface.Invalidate();
            pnlTrackSurface.Controls.Add(vScroll);

            timerPlay = new Timer { Interval = 30 };
            timerPlay.Tick += timerPlay_Tick;

            Resize += (s, e) => LayoutAll();
            pnlRuler.BringToFront();
        }

        private ContextMenuStrip CreateClipContextMenu()
        {
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Copia", null, (s, e) => CopySelectedClips());
            ctx.Items.Add("Taglia", null, (s, e) => CutSelectedClips());
            ctx.Items.Add("Incolla", null, (s, e) => PasteClips());
            ctx.Items.Add("-");
            ctx.Items.Add("Modifica...", null, (s, e) => EditSelectedClip());
            ctx.Items.Add("Separa", null, (s, e) => SplitSelectedClip());
            ctx.Items.Add("Unisci", null, (s, e) => MergeSelectedClips());
            return ctx;
        }

        private void BuildMenu()
        {
            menuStripMain = new MenuStrip { Dock = DockStyle.Top, BackColor = Color.FromArgb(45, 49, 58), ForeColor = Color.White, Renderer = new DarkMenuRenderer() };
            menuFile = new ToolStripMenuItem("File");

            var menuNuovo = new ToolStripMenuItem("Nuovo Progetto");
            menuNuovo.Click += (s, e) => {
                StopPlaybackInternal(true);
                clips.Clear();
                selectedClipIndex = -1;
                selectedClipIndices.Clear();
                UpdateProjectLength();
                RebuildMixerTracks();
                LayoutAll();
            };
            menuFile.DropDownItems.Add(menuNuovo);
            menuFile.DropDownItems.Add(new ToolStripSeparator());

            var menuSalva = new ToolStripMenuItem("Salva Progetto...");
            menuSalva.Click += (s, e) => SaveProject();
            menuFile.DropDownItems.Add(menuSalva);

            var menuCarica = new ToolStripMenuItem("Carica Progetto...");
            menuCarica.Click += (s, e) => LoadProject();
            menuFile.DropDownItems.Add(menuCarica);
            menuFile.DropDownItems.Add(new ToolStripSeparator());

            menuImporta = new ToolStripMenuItem("Importa Audio...");
            menuImporta.Click += (s, e) => ImportAudioFile();
            menuFile.DropDownItems.Add(menuImporta);

            menuStripMain.Items.Add(menuFile);

            menuAggiungi = new ToolStripMenuItem("Aggiungi");
            menuStripMain.Items.Add(menuAggiungi);
            RefreshAggiungiMenu();

            MainMenuStrip = menuStripMain;
            Controls.Add(menuStripMain);
        }

        private void RefreshAggiungiMenu()
        {
            menuAggiungi.DropDownItems.Clear();

            var menuBatteria = new ToolStripMenuItem("Batteria (DrumKit)");
            menuBatteria.Click += (s, e) => { var frm = new DrumKitForm(); frm.Show(this); };
            menuAggiungi.DropDownItems.Add(menuBatteria);
            menuAggiungi.DropDownItems.Add(new ToolStripSeparator());

            if (!Directory.Exists(StrumentiPath)) { menuAggiungi.DropDownItems.Add(new ToolStripMenuItem("(nessuno strumento trovato)") { Enabled = false }); return; }
            var dirs = Directory.GetDirectories(StrumentiPath).OrderBy(d => d).ToArray();
            if (dirs.Length == 0) { menuAggiungi.DropDownItems.Add(new ToolStripMenuItem("(nessuno strumento trovato)") { Enabled = false }); return; }

            foreach (string dir in dirs)
            {
                string instrumentName = Path.GetFileName(dir);
                if (instrumentName.Equals("Batteria", StringComparison.OrdinalIgnoreCase)) continue;

                string samplePath = Path.Combine(dir, "C4.wav");
                if (!File.Exists(samplePath)) continue;
                var item = new ToolStripMenuItem(instrumentName) { Tag = instrumentName };
                item.Click += (s, e) => OpenInstrumentForm((string)((ToolStripMenuItem)s).Tag);
                menuAggiungi.DropDownItems.Add(item);
            }
            if (menuAggiungi.DropDownItems.Count == 2) menuAggiungi.DropDownItems.Add(new ToolStripMenuItem("(nessun C4.wav trovato)") { Enabled = false });
        }

        private void OpenInstrumentForm(string instrumentName)
        {
            string samplePath = Path.Combine(StrumentiPath, instrumentName, "C4.wav");
            if (!File.Exists(samplePath)) { MessageBox.Show("Sample non trovato", "Errore"); return; }
            var form = new InstrumentForm(instrumentName, samplePath);
            form.Show(this);
        }

        private void BuildTopBar()
        {
            var lblTitle = new Label { Text = "MyDAW - C# WinForms + NAudio", ForeColor = Color.Gainsboro, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(12, 12) };
            pnlTopBar.Controls.Add(lblTitle);

            btnPlay = MakeTopButton("▶"); btnStop = MakeTopButton("■"); btnRec = MakeTopButton("●");
            btnRec.ForeColor = Color.IndianRed;
            btnPlay.Location = new Point(300, 8); btnStop.Location = new Point(340, 8); btnRec.Location = new Point(380, 8);

            pnlTopBar.Controls.Add(btnPlay); pnlTopBar.Controls.Add(btnStop); pnlTopBar.Controls.Add(btnRec);

            btnPlay.Click += (s, e) => StartPlayback();
            btnStop.Click += (s, e) => StopAndReturnToStart();
            btnRec.Click += (s, e) => ToggleRecord();
        }

        private Button MakeTopButton(string text)
        {
            var b = new Button { Text = text, Width = 34, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 60, 72), ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void ImportAudioFile()
        {
            string startDir = Directory.Exists(ImportFolderPath) ? ImportFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            using (var ofd = new OpenFileDialog { Title = "Importa registrazione", InitialDirectory = startDir, Filter = "File audio|*.wav;*.mp3|Tutti i file|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK) AddImportedClipToFirstFreeTrack(ofd.FileName);
            }
        }

        private void AddImportedClipToFirstFreeTrack(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            float durationSec;
            using (var reader = new AudioFileReader(filePath)) durationSec = (float)reader.TotalTime.TotalSeconds;
            if (durationSec < 0.05f) durationSec = 0.05f;

            var newClip = new AudioClipInfo { FilePath = filePath, DisplayName = Path.GetFileNameWithoutExtension(filePath), StartSec = 0f, DurationSec = durationSec, LaneIndex = GetFirstFreeLaneIndex(), Volume = 1f };
            clips.Add(newClip);
            selectedClipIndex = clips.IndexOf(newClip);
            selectedClipIndices = new List<int> { selectedClipIndex };
            FinalizeClipAdd(newClip);
        }

        public void AddMelodyClip(string filePath, float patternDuration, List<SequencerNote> notes, int totalSteps, float stepSec, bool isDrumKit)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            var newClip = new AudioClipInfo
            {
                FilePath = filePath,
                DisplayName = Path.GetFileNameWithoutExtension(filePath) + (isDrumKit ? " (batteria)" : " (melodia)"),
                StartSec = 0f,
                DurationSec = patternDuration,
                LaneIndex = GetFirstFreeLaneIndex(),
                Volume = 1f,
                IsLoopClip = true,
                LoopLengthSec = patternDuration,
                PianoNotes = new List<SequencerNote>(notes),
                TotalSteps = totalSteps,
                StepLengthSec = stepSec,
                IsDrumKit = isDrumKit
            };
            clips.Add(newClip);
            selectedClipIndex = clips.IndexOf(newClip);
            selectedClipIndices = new List<int> { selectedClipIndex };
            FinalizeClipAdd(newClip);
        }

        private void FinalizeClipAdd(AudioClipInfo newClip)
        {
            UpdateProjectLength();
            playheadSec = 0f; showPlayhead = false; timelineOffsetX = 0;
            if (hScroll != null) hScroll.Value = hScroll.Minimum;
            if (vScroll != null) vScroll.Value = vScroll.Minimum;
            RebuildMixerTracks(); LayoutAll(); pnlRuler.Refresh(); pnlTrackSurface.Refresh();
            if (!newClip.PeaksReady && newClip.PianoNotes == null) BuildWaveformPeaksAsync(newClip, 2500);
        }

        private int GetFirstFreeLaneIndex() { int lane = 0; while (clips.Any(c => c.LaneIndex == lane)) lane++; return lane; }

        private void BuildMixer()
        {
            pnlMixerTracks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10), BackColor = Color.FromArgb(35, 38, 46) };
            pnlBottomMixer.Controls.Add(pnlMixerTracks);
        }

        private void RebuildMixerTracks()
        {
            pnlMixerTracks.SuspendLayout(); pnlMixerTracks.Controls.Clear();
            if (clips.Count == 0) { pnlMixerTracks.Controls.Add(new Label { Text = "Nessuna traccia registrata", ForeColor = Color.Gainsboro, AutoSize = true, Margin = new Padding(10) }); pnlMixerTracks.ResumeLayout(); return; }

            foreach (var clip in clips.OrderBy(c => c.LaneIndex))
            {
                int clipIndex = clips.IndexOf(clip);
                bool isSelected = selectedClipIndices.Contains(clipIndex);
                var strip = new Panel { Width = 120, Height = 180, BackColor = isSelected ? Color.FromArgb(75, 90, 120) : (clipIndex == selectedClipIndex ? Color.FromArgb(55, 65, 95) : Color.FromArgb(30, 33, 40)), Margin = new Padding(0, 0, 10, 0), Tag = clipIndex };
                var lblTrack = new Label { Text = $"TRACK {clip.LaneIndex + 1}", ForeColor = Color.Gainsboro, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 10), Tag = clipIndex };
                var lblName = new Label { Text = clip.DisplayName, ForeColor = Color.Silver, AutoSize = false, Width = 100, Height = 30, Location = new Point(10, 32), AutoEllipsis = true, Tag = clipIndex };
                var lblVol = new Label { Text = $"VOL: {(int)(clip.Volume * 100)}%", ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(10, 62), Tag = clipIndex };

                var trk = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, (int)(clip.Volume * 100))), TickStyle = TickStyle.None, Height = 100, Width = 40, Location = new Point(35, 82) };
                trk.Scroll += (s, e) => { clip.Volume = trk.Value / 100f; lblVol.Text = $"VOL: {trk.Value}%"; foreach (var reader in clip.ActiveReaders) if (reader != null) reader.Volume = clip.Volume; };

                foreach (Control c in new Control[] { strip, lblTrack, lblName, lblVol }) c.MouseDown += MixerTrack_MouseDown;
                strip.Controls.Add(lblTrack); strip.Controls.Add(lblName); strip.Controls.Add(lblVol); strip.Controls.Add(trk);
                pnlMixerTracks.Controls.Add(strip);
            }
            pnlMixerTracks.ResumeLayout();
        }

        private void MixerTrack_MouseDown(object sender, MouseEventArgs e)
        {
            if (sender is Control c && c.Tag is int idx)
            {
                SelectClip(idx, true);
                pnlTrackSurface.Focus();
            }
        }

        private void ToggleRecord()
        {
            if (!isRecording) { StopPlaybackInternal(true); StartRecording(); } else StopRecording();
        }

        private void StartRecording()
        {
            Directory.CreateDirectory(ImportFolderPath);
            lastRecord = Path.Combine(ImportFolderPath, "Rec_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".wav");
            waveSource = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1) };
            waveSource.DataAvailable += WaveSource_DataAvailable;
            waveSource.RecordingStopped += WaveSource_RecordingStopped;
            waveFile = new WaveFileWriter(lastRecord, waveSource.WaveFormat);

            isRecording = true; btnRec.BackColor = Color.FromArgb(120, 40, 40);
            showPlayhead = false; playheadSec = 0f; timelineOffsetX = 0;
            if (hScroll != null) hScroll.Value = hScroll.Minimum;
            waveSource.StartRecording(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void StopRecording() { if (!isRecording) return; isRecording = false; try { waveSource?.StopRecording(); } catch { } btnRec.BackColor = Color.FromArgb(55, 60, 72); }
        private void WaveSource_DataAvailable(object sender, WaveInEventArgs e) { waveFile?.Write(e.Buffer, 0, e.BytesRecorded); waveFile?.Flush(); }

        private void WaveSource_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => WaveSource_RecordingStopped(sender, e))); return; }
            waveFile?.Dispose(); waveFile = null; waveSource?.Dispose(); waveSource = null;
            if (!string.IsNullOrEmpty(lastRecord) && File.Exists(lastRecord)) AddImportedClipToFirstFreeTrack(lastRecord);
        }

        private void BuildWaveformPeaksAsync(AudioClipInfo clip, int maxPeaks)
        {
            string fp = clip.FilePath;
            Task.Run(() => { var peaks = BuildWaveformPeaks(fp, maxPeaks); if (IsHandleCreated) BeginInvoke(new Action(() => { clip.Peaks = peaks; clip.PeaksReady = true; pnlTrackSurface.Invalidate(); })); });
        }

        private List<float> BuildWaveformPeaks(string filePath, int maxPeaks)
        {
            var peaks = new List<float>();
            try
            {
                using (var reader = new AudioFileReader(filePath))
                {
                    int ch = reader.WaveFormat.Channels;
                    long tot = (long)(reader.TotalTime.TotalSeconds * reader.WaveFormat.SampleRate);
                    int spp = (int)Math.Max(1, tot / Math.Max(1, maxPeaks));
                    float[] buf = new float[8192 * ch];
                    int read; int fc = 0; float cp = 0f;

                    while ((read = reader.Read(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < read; i += ch)
                        {
                            float sa = 0f;
                            for (int c2 = 0; c2 < ch && (i + c2) < read; c2++) { float s = Math.Abs(buf[i + c2]); if (s > sa) sa = s; }
                            if (sa > cp) cp = sa;
                            if (++fc >= spp) { peaks.Add(Math.Min(1f, cp)); cp = 0f; fc = 0; }
                        }
                    }
                    if (fc > 0) peaks.Add(Math.Min(1f, cp));
                }
            }
            catch { }
            if (peaks.Count == 0) peaks.Add(0f);
            return peaks;
        }

        private ISampleProvider ConvertToMixerFormat(ISampleProvider input)
        {
            if (input.WaveFormat.Channels == 1 && MixerChannels == 2) input = new MonoToStereoSampleProvider(input);
            if (input.WaveFormat.SampleRate != MixerSampleRate) input = new WdlResamplingSampleProvider(input, MixerSampleRate);
            return input;
        }

        private void StartPlayback()
        {
            if (clips.Count == 0) return;
            if (isRecording) StopRecording();
            StopPlaybackInternal(false);

            float projectEnd = GetProjectEndSec();
            if (playheadSec >= projectEnd) { playheadSec = 0f; timelineOffsetX = 0; if (hScroll != null) hScroll.Value = hScroll.Minimum; }

            try
            {
                // Creazione del mixer compatibile con la versione di NAudio usata:
                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixerSampleRate, MixerChannels)) { ReadFully = false };
                int mixerInputCount = 0;

                foreach (var clip in clips)
                {
                    if (!File.Exists(clip.FilePath)) continue;
                    clip.ActiveReaders.Clear();

                    float clipEnd = clip.StartSec + clip.DurationSec;
                    if (playheadSec >= clipEnd) continue;

                    if (clip.IsLoopClip && clip.LoopLengthSec > 0)
                    {
                        float pattern = clip.LoopLengthSec;
                        float currentLoopStart = clip.StartSec;

                        while (currentLoopStart < clipEnd)
                        {
                            float loopEnd = Math.Min(currentLoopStart + pattern, clipEnd);
                            if (loopEnd <= playheadSec) { currentLoopStart += pattern; continue; }

                            float delayFromNow = currentLoopStart - playheadSec;
                            float skipInFile = 0f;
                            if (delayFromNow < 0) { skipInFile = -delayFromNow; delayFromNow = 0f; }

                            float durationToPlay = loopEnd - Math.Max(playheadSec, currentLoopStart);

                            var reader = new AudioFileReader(clip.FilePath) { Volume = clip.Volume };
                            reader.CurrentTime = TimeSpan.FromSeconds(skipInFile);
                            clip.ActiveReaders.Add(reader);

                            var offset = new OffsetSampleProvider(ConvertToMixerFormat(reader))
                            {
                                Take = TimeSpan.FromSeconds(durationToPlay),
                                DelayBy = TimeSpan.FromSeconds(delayFromNow)
                            };
                            mixer.AddMixerInput(offset);
                            mixerInputCount++;

                            currentLoopStart += pattern;
                        }
                        continue;
                    }

                    var normalReader = new AudioFileReader(clip.FilePath) { Volume = clip.Volume };
                    clip.ActiveReaders.Add(normalReader);

                    float ls = playheadSec - clip.StartSec;
                    if (ls > 0)
                    {
                        normalReader.CurrentTime = TimeSpan.FromSeconds(ls);
                        var offset = new OffsetSampleProvider(ConvertToMixerFormat(normalReader))
                        {
                            Take = TimeSpan.FromSeconds(clip.DurationSec - ls)
                        };
                        mixer.AddMixerInput(offset);
                        mixerInputCount++;
                    }
                    else
                    {
                        var offset = new OffsetSampleProvider(ConvertToMixerFormat(normalReader))
                        {
                            DelayBy = TimeSpan.FromSeconds(-ls),
                            Take = TimeSpan.FromSeconds(clip.DurationSec)
                        };
                        mixer.AddMixerInput(offset);
                        mixerInputCount++;
                    }
                }

                if (mixerInputCount == 0) { DisposePlaybackResources(false); return; }

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.PlaybackStopped += OutputDevice_PlaybackStopped;
                playbackStartSec = playheadSec;
                playbackStartUtc = DateTime.UtcNow;
                outputDevice.Play();
                showPlayhead = true;
                timerPlay.Start();
                LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
            }
            catch (Exception ex) { timerPlay.Stop(); DisposePlaybackResources(false); MessageBox.Show("Errore playback:\n" + ex.Message, "Errore"); }
        }

        private void OutputDevice_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OutputDevice_PlaybackStopped(sender, e))); return; }
            timerPlay.Stop();
            playheadSec = Math.Min(GetProjectEndSec(), playbackStartSec + (float)(DateTime.UtcNow - playbackStartUtc).TotalSeconds);
            DisposePlaybackResources(false);
            pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void StopAndReturnToStart() { timerPlay.Stop(); StopPlaybackInternal(true); playheadSec = 0f; showPlayhead = false; timelineOffsetX = 0; if (hScroll != null) hScroll.Value = hScroll.Minimum; pnlRuler.Invalidate(); pnlTrackSurface.Invalidate(); }
        private void StopPlaybackInternal(bool resetToZero) { if (outputDevice != null) { outputDevice.PlaybackStopped -= OutputDevice_PlaybackStopped; try { if (outputDevice.PlaybackState != PlaybackState.Stopped) outputDevice.Stop(); } catch { } } DisposePlaybackResources(resetToZero); }

        private void DisposePlaybackResources(bool resetToZero)
        {
            outputDevice?.Dispose(); outputDevice = null;
            foreach (var clip in clips) { foreach (var reader in clip.ActiveReaders) reader?.Dispose(); clip.ActiveReaders.Clear(); }
            if (resetToZero) playheadSec = 0f;
        }

        private float GetProjectEndSec() => clips.Count == 0 ? 0f : clips.Max(c => c.StartSec + c.DurationSec);
        private void UpdateProjectLength() { timelineSeconds = clips.Count == 0 ? 10f : Math.Max(10f, GetProjectEndSec()); }

        private void timerPlay_Tick(object sender, EventArgs e)
        {
            if (outputDevice == null || outputDevice.PlaybackState != PlaybackState.Playing) return;
            playheadSec = playbackStartSec + (float)(DateTime.UtcNow - playbackStartUtc).TotalSeconds;
            float pe = GetProjectEndSec(); if (playheadSec > pe) playheadSec = pe;
            showPlayhead = true;
            AutoScrollToPlayhead();
            pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void Timeline_MouseDownSeek(object sender, MouseEventArgs e)
        {
            pnlTrackSurface.Focus();

            if (sender == pnlTrackSurface)
            {
                int hit = HitTestClip(e.Location);
                if (hit >= 0)
                {
                    SelectClip(hit, true);
                    var rect = GetClipRectangle(clips[hit]);

                    const int edgeTolerance = 8;
                    if (e.X >= rect.Right - edgeTolerance && e.X <= rect.Right + edgeTolerance)
                    {
                        isDraggingRightEdge = true; draggingClipIndex = hit; dragStartMouseX = e.X; dragStartDurationSec = clips[hit].DurationSec;
                        pnlTrackSurface.Capture = true; pnlTrackSurface.Cursor = Cursors.SizeWE;
                        return;
                    }

                    if (!isRecording && (outputDevice == null || outputDevice.PlaybackState != PlaybackState.Playing))
                    {
                        isDraggingClip = true; draggingClipIndex = hit; dragStartMouseX = e.X; dragStartClipSec = clips[hit].StartSec;
                        pnlTrackSurface.Capture = true; pnlTrackSurface.Cursor = Cursors.SizeWE;
                    }
                    return;
                }
                else
                {
                    // cliccato fuori dai clip -> deseleziona se non Ctrl premuto
                    if (!ModifierKeys.HasFlag(Keys.Control))
                        selectedClipIndices.Clear();
                }
                int lane = GetLaneFromY(e.Y);
                if (lane >= 0)
                {
                    int clipIdx = clips.FindIndex(c => c.LaneIndex == lane);
                    if (clipIdx >= 0) SelectClip(clipIdx, true);
                    else selectedClipIndices.Clear();
                }
            }

            int x = e.X - HeaderW; if (x < 0) { pnlTrackSurface.Invalidate(); return; }
            float sec = (x + timelineOffsetX) / (float)pixelsPerSecond;
            playheadSec = Math.Max(0, Math.Min(sec, timelineSeconds)); showPlayhead = true;
            if (outputDevice != null && outputDevice.PlaybackState == PlaybackState.Playing) { StartPlayback(); return; }
            AutoScrollToPlayhead(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void PnlTrackSurface_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingRightEdge && draggingClipIndex >= 0)
            {
                float deltaSec = (e.X - dragStartMouseX) / (float)pixelsPerSecond;
                float newDur = dragStartDurationSec + deltaSec;
                if (newDur < 0.1f) newDur = 0.1f;

                var clip = clips[draggingClipIndex];

                if (clip.IsLoopClip && clip.StepLengthSec > 0)
                {
                    newDur = (float)Math.Round(newDur / clip.StepLengthSec) * clip.StepLengthSec;
                    if (newDur < clip.StepLengthSec) newDur = clip.StepLengthSec;
                }

                clip.DurationSec = newDur;
                UpdateProjectLength(); LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
                return;
            }

            if (!isDraggingClip || draggingClipIndex < 0 || draggingClipIndex >= clips.Count) return;
            clips[draggingClipIndex].StartSec = (float)Math.Round(Math.Max(0f, dragStartClipSec + (e.X - dragStartMouseX) / (float)pixelsPerSecond), 2);
            UpdateProjectLength(); LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void PnlTrackSurface_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDraggingRightEdge) { isDraggingRightEdge = false; draggingClipIndex = -1; pnlTrackSurface.Capture = false; pnlTrackSurface.Cursor = Cursors.Default; UpdateProjectLength(); LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate(); return; }
            if (!isDraggingClip) return;
            isDraggingClip = false; draggingClipIndex = -1; pnlTrackSurface.Capture = false; pnlTrackSurface.Cursor = Cursors.Default;
            UpdateProjectLength(); LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private int HitTestClip(Point p) { for (int i = clips.Count - 1; i >= 0; i--) if (GetClipRectangle(clips[i]).Contains(p)) return i; return -1; }
        private Rectangle GetClipRectangle(AudioClipInfo clip) { return new Rectangle(HeaderW + (int)(clip.StartSec * pixelsPerSecond) - timelineOffsetX, TrackTopPadding + clip.LaneIndex * rowHeight + 8 - vScroll.Value, Math.Max(20, (int)(clip.DurationSec * pixelsPerSecond)), rowHeight - 16); }
        private int GetLaneFromY(int y) { int yy = y + vScroll.Value - TrackTopPadding; return yy < 0 ? -1 : yy / rowHeight; }

        private void SelectClip(int index, bool addToSelection = false)
        {
            if (index < 0 || index >= clips.Count) return;

            if (!addToSelection && !ModifierKeys.HasFlag(Keys.Control))
                selectedClipIndices.Clear();

            if (ModifierKeys.HasFlag(Keys.Control) && addToSelection)
            {
                if (selectedClipIndices.Contains(index))
                    selectedClipIndices.Remove(index);
                else
                    selectedClipIndices.Add(index);
            }
            else if (!addToSelection)
            {
                selectedClipIndices.Clear();
                selectedClipIndices.Add(index);
            }

            selectedClipIndex = (selectedClipIndices.Count == 1) ? selectedClipIndices[0] : -1;

            RebuildMixerTracks();
            pnlTrackSurface.Invalidate();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteSelectedClips(); e.Handled = true; e.SuppressKeyPress = true; } }

        private void DeleteSelectedClips()
        {
            if (selectedClipIndices.Count == 0) return;
            StopPlaybackInternal(false); timerPlay.Stop();
            foreach (int idx in selectedClipIndices.OrderByDescending(i => i))
                clips.RemoveAt(idx);
            selectedClipIndices.Clear();
            selectedClipIndex = -1;

            if (clips.Count == 0)
            {
                playheadSec = 0f; showPlayhead = false; timelineSeconds = 10f; timelineOffsetX = 0;
                if (hScroll != null) hScroll.Value = hScroll.Minimum;
                if (vScroll != null) vScroll.Value = vScroll.Minimum;
            }
            else
            {
                UpdateProjectLength();
                float pe = GetProjectEndSec(); if (playheadSec > pe) playheadSec = pe;
            }

            RebuildMixerTracks(); LayoutAll(); pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void AutoScrollToPlayhead()
        {
            int tvW = Math.Max(1, pnlCenter.ClientSize.Width - vScroll.Width - HeaderW);
            int ppx = (int)(playheadSec * pixelsPerSecond); int xOS = HeaderW + ppx - timelineOffsetX;
            if (xOS > HeaderW + tvW - 20) { int no = Math.Max(0, Math.Min(ppx - (tvW - 20), hScroll.Maximum)); if (hScroll.Value != no) { hScroll.Value = no; timelineOffsetX = no; } }
        }

        private void LayoutAll()
        {
            int maxLane = clips.Count == 0 ? 0 : clips.Max(c => c.LaneIndex);
            int contentH = TrackTopPadding + rowHeight * Math.Max(7, maxLane + 2);
            vScroll.Minimum = 0; vScroll.LargeChange = Math.Max(1, pnlTrackSurface.ClientSize.Height); vScroll.Maximum = Math.Max(0, contentH - 1); vScroll.SmallChange = rowHeight;
            if (vScroll.Value > vScroll.Maximum) vScroll.Value = vScroll.Maximum;

            int totalW = (int)(timelineSeconds * pixelsPerSecond);
            hScroll.Minimum = 0; hScroll.LargeChange = Math.Max(1, pnlCenter.ClientSize.Width - vScroll.Width); hScroll.Maximum = Math.Max(0, totalW - 1);
            if (hScroll.Value > hScroll.Maximum) hScroll.Value = hScroll.Maximum;

            pnlRuler.Invalidate(); pnlTrackSurface.Invalidate();
        }

        private void PnlRuler_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(38, 41, 50));
            using (var p1 = new Pen(Color.FromArgb(70, 75, 90))) g.DrawLine(p1, HeaderW, pnlRuler.Height - 1, pnlRuler.Width, pnlRuler.Height - 1);
            using (var p2 = new Pen(Color.FromArgb(55, 60, 70))) g.DrawLine(p2, HeaderW, 0, HeaderW, pnlRuler.Height);
            using var f = new Font("Segoe UI", 9, FontStyle.Regular); using var br = new SolidBrush(Color.Gainsboro);

            int startX = HeaderW - timelineOffsetX; int secs = (int)Math.Ceiling(timelineSeconds);
            for (int s = 0; s <= secs; s++)
            {
                int x = startX + s * pixelsPerSecond;
                if (x < HeaderW) continue; if (x > pnlRuler.Width) break;
                g.DrawLine(Pens.Gray, x, 6, x, pnlRuler.Height - 6);
                if (s % 2 == 0) g.DrawString(s.ToString(), f, br, x + 3, 10);
            }
            if (showPlayhead) { int xp = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX; using var pp = new Pen(Color.WhiteSmoke, 2); g.DrawLine(pp, xp, 0, xp, pnlRuler.Height); }
        }

        private void PnlTrackSurface_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = pnlTrackSurface.ClientSize.Width - vScroll.Width; int h = pnlTrackSurface.ClientSize.Height;
            int totalRows = Math.Max(7, clips.Count == 0 ? 2 : clips.Max(c => c.LaneIndex) + 2);

            using (var hb = new SolidBrush(Color.FromArgb(30, 33, 40))) g.FillRectangle(hb, 0, 0, HeaderW, h);
            if (selectedClipIndex >= 0 && selectedClipIndex < clips.Count) { int selY = TrackTopPadding + clips[selectedClipIndex].LaneIndex * rowHeight - vScroll.Value; using var sb = new SolidBrush(Color.FromArgb(35, 70, 110, 150)); g.FillRectangle(sb, 0, selY, w, rowHeight); }
            using var penRow = new Pen(Color.FromArgb(40, 45, 55)); for (int i = 0; i <= totalRows; i++) g.DrawLine(penRow, 0, TrackTopPadding + i * rowHeight - vScroll.Value, w, TrackTopPadding + i * rowHeight - vScroll.Value);
            using (var ph = new Pen(Color.FromArgb(55, 60, 70))) g.DrawLine(ph, HeaderW, 0, HeaderW, h);
            using var penSec = new Pen(Color.FromArgb(35, 40, 48)); for (int s = 0; s <= (int)Math.Ceiling(timelineSeconds); s++) { int x = HeaderW + s * pixelsPerSecond - timelineOffsetX; if (x >= HeaderW && x <= w) g.DrawLine(penSec, x, 0, x, h); }

            using var tf = new Font("Segoe UI", 9, FontStyle.Bold);
            for (int i = 0; i < totalRows; i++) { int y = TrackTopPadding + i * rowHeight - vScroll.Value; if (y + rowHeight < 0 || y > h) continue; bool sel = selectedClipIndices.Any(idx => clips[idx].LaneIndex == i); using var tb = new SolidBrush(sel ? Color.White : Color.Gainsboro); g.DrawString($"TRACK {i + 1}", tf, tb, 10, y + 10); }
            for (int i = 0; i < clips.Count; i++) { var rect = GetClipRectangle(clips[i]); if (rect.Bottom >= 0 && rect.Top <= h) DrawAudioClip(g, rect, clips[i], selectedClipIndices.Contains(i)); }
            if (showPlayhead) { int xp = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX; using var pp = new Pen(Color.WhiteSmoke, 2); g.DrawLine(pp, xp, 0, xp, h); }
        }

        private void DrawAudioClip(Graphics g, Rectangle rect, AudioClipInfo clip, bool sel)
        {
            if (rect.Right < HeaderW || rect.Left > pnlTrackSurface.Width) return;
            using var cb = new SolidBrush(sel ? Color.FromArgb(70, 105, 185) : Color.FromArgb(48, 76, 140)); using var cp = new Pen(sel ? Color.FromArgb(255, 230, 140) : Color.FromArgb(110, 150, 240), sel ? 2 : 1);
            using var wp = new Pen(Color.FromArgb(220, 235, 255), 1); using var ctp = new Pen(Color.FromArgb(90, 130, 210), 1);
            using var tb = new SolidBrush(Color.WhiteSmoke); using var tf = new Font("Segoe UI", 8, FontStyle.Bold);

            g.FillRectangle(cb, rect); g.DrawRectangle(cp, rect); int cy = rect.Top + rect.Height / 2; g.DrawLine(ctp, rect.Left + 1, cy, rect.Right - 1, cy);

            if (clip.IsLoopClip && clip.PianoNotes != null && clip.PianoNotes.Count > 0)
            {
                int patternPx = Math.Max(10, (int)(clip.LoopLengthSec * pixelsPerSecond));
                int repeats = (int)Math.Ceiling((double)rect.Width / patternPx);

                for (int r = 0; r < repeats; r++)
                {
                    int subLeft = rect.Left + r * patternPx;
                    int subW = Math.Min(patternPx, rect.Right - subLeft);
                    if (subW <= 0) break;

                    DrawMiniPianoRoll(g, new Rectangle(subLeft, rect.Top, subW, rect.Height), clip, patternPx);
                }
            }
            else if (clip.PeaksReady && clip.Peaks?.Count > 0 && rect.Width > 2)
            {
                DrawWaveformSegment(g, rect, clip.Peaks, wp, ctp);
            }

            g.DrawString($"{clip.DisplayName} ({clip.DurationSec:0.0}s)", tf, tb, rect.Left + 6, rect.Top + 4);
        }

        private void DrawMiniPianoRoll(Graphics g, Rectangle rect, AudioClipInfo clip, int patternPx)
        {
            if (clip.TotalSteps <= 0) return;
            float stepW = (float)patternPx / clip.TotalSteps;

            var layoutNames = clip.IsDrumKit ? DrumPieces : NoteNames;
            float noteH = (float)rect.Height / layoutNames.Length;

            using var noteBrush = new SolidBrush(clip.IsDrumKit ? Color.FromArgb(255, 180, 100) : Color.FromArgb(200, 220, 255));
            using var borderPen = new Pen(clip.IsDrumKit ? Color.FromArgb(255, 140, 50) : Color.FromArgb(100, 150, 255), 1);

            foreach (var n in clip.PianoNotes)
            {
                int noteIdx = Array.IndexOf(layoutNames, n.Note);
                if (noteIdx < 0) continue;

                float nx = rect.Left + n.Step * stepW;
                float ny = rect.Top + noteIdx * noteH;
                float nw = n.Length * stepW;

                if (nx > rect.Right) continue;
                if (nx + nw > rect.Right) nw = rect.Right - nx;

                var noteRect = new RectangleF(nx, ny, nw, Math.Max(1, noteH));
                g.FillRectangle(noteBrush, noteRect);
                g.DrawRectangle(borderPen, nx, ny, nw, Math.Max(1, noteH));
            }
        }

        private void DrawWaveformSegment(Graphics g, Rectangle rect, List<float> peaks, Pen wavePen, Pen centerPen)
        {
            if (peaks.Count == 0) return;
            int pc = peaks.Count; int hh = Math.Max(1, rect.Height / 2 - 4); int centerY = rect.Top + rect.Height / 2;
            for (int px = 0; px < rect.Width; px++)
            {
                int si = (int)(px * pc / (float)rect.Width); int ei = Math.Min(pc, (int)((px + 1) * pc / (float)rect.Width));
                float mx = 0f; for (int i = si; i < ei; i++) if (peaks[i] > mx) mx = peaks[i];
                int amp = (int)(mx * hh); g.DrawLine(wavePen, rect.Left + px, centerY - amp, rect.Left + px, centerY + amp);
            }
        }

        private void SaveProject()
        {
            try
            {
                Directory.CreateDirectory(ProgettiPath);
                using (var sfd = new SaveFileDialog { InitialDirectory = ProgettiPath, Filter = "Progetto DAW (*.daw)|*.daw", DefaultExt = "daw" })
                {
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        var saveData = clips.Select(c => new AudioClipSaveData
                        {
                            FilePath = c.FilePath,
                            DisplayName = c.DisplayName,
                            StartSec = c.StartSec,
                            DurationSec = c.DurationSec,
                            LaneIndex = c.LaneIndex,
                            Volume = c.Volume,
                            IsLoopClip = c.IsLoopClip,
                            LoopLengthSec = c.LoopLengthSec,
                            TotalSteps = c.TotalSteps,
                            StepLengthSec = c.StepLengthSec,
                            IsDrumKit = c.IsDrumKit,
                            PianoNotes = c.PianoNotes?.ToList()
                        }).ToList();

                        var serializer = new XmlSerializer(typeof(List<AudioClipSaveData>));
                        using (var stream = new StreamWriter(sfd.FileName))
                        {
                            serializer.Serialize(stream, saveData);
                        }
                        MessageBox.Show("Progetto salvato con successo!", "Salvataggio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il salvataggio:\n" + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadProject()
        {
            try
            {
                Directory.CreateDirectory(ProgettiPath);
                using (var ofd = new OpenFileDialog { InitialDirectory = ProgettiPath, Filter = "Progetto DAW (*.daw)|*.daw" })
                {
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        StopPlaybackInternal(true);
                        clips.Clear();
                        selectedClipIndices.Clear();

                        var serializer = new XmlSerializer(typeof(List<AudioClipSaveData>));
                        List<AudioClipSaveData> loadedData;
                        using (var stream = new StreamReader(ofd.FileName))
                        {
                            loadedData = (List<AudioClipSaveData>)serializer.Deserialize(stream);
                        }

                        foreach (var data in loadedData)
                        {
                            var clip = new AudioClipInfo
                            {
                                FilePath = data.FilePath,
                                DisplayName = data.DisplayName,
                                StartSec = data.StartSec,
                                DurationSec = data.DurationSec,
                                LaneIndex = data.LaneIndex,
                                Volume = data.Volume,
                                IsLoopClip = data.IsLoopClip,
                                LoopLengthSec = data.LoopLengthSec,
                                TotalSteps = data.TotalSteps,
                                StepLengthSec = data.StepLengthSec,
                                IsDrumKit = data.IsDrumKit,
                                PianoNotes = data.PianoNotes?.ToList()
                            };

                            clips.Add(clip);
                            if (!clip.IsLoopClip || clip.PianoNotes == null)
                            {
                                BuildWaveformPeaksAsync(clip, 2500);
                            }
                        }

                        selectedClipIndex = -1;
                        UpdateProjectLength();
                        playheadSec = 0f; showPlayhead = false; timelineOffsetX = 0;
                        if (hScroll != null) hScroll.Value = hScroll.Minimum;
                        if (vScroll != null) vScroll.Value = vScroll.Minimum;

                        RebuildMixerTracks();
                        LayoutAll();
                        pnlRuler.Refresh();
                        pnlTrackSurface.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento:\n" + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==========================================
        // NUOVE FUNZIONI: COPIA, TAGLIA, INCOLLA, ECC.
        // ==========================================
        private void CopySelectedClips()
        {
            if (selectedClipIndices.Count == 0) return;
            clipClipboard = selectedClipIndices.Select(i => CloneClip(clips[i])).ToList();
            cutPending = false;
        }

        private void CutSelectedClips()
        {
            CopySelectedClips();
            cutPending = true;
            foreach (var idx in selectedClipIndices.OrderByDescending(i => i))
                clips.RemoveAt(idx);
            selectedClipIndices.Clear();
            selectedClipIndex = -1;
            UpdateProjectLength();
            RebuildMixerTracks();
            pnlTrackSurface.Invalidate();
        }

        private void PasteClips()
        {
            if (clipClipboard == null || clipClipboard.Count == 0) return;

            int targetLane = GetFirstFreeLaneIndex();
            float startOffset = 0f;

            if (selectedClipIndices.Count > 0)
            {
                var lastClip = clips[selectedClipIndices.Max()];
                startOffset = lastClip.StartSec + lastClip.DurationSec;
                targetLane = lastClip.LaneIndex;
            }

            foreach (var clipData in clipClipboard)
            {
                var newClip = CloneClip(clipData);
                newClip.StartSec = startOffset;
                newClip.LaneIndex = targetLane++;
                clips.Add(newClip);
                if (!newClip.PeaksReady && newClip.PianoNotes == null)
                    BuildWaveformPeaksAsync(newClip, 2500);
            }

            if (cutPending)
            {
                clipClipboard = null;
                cutPending = false;
            }

            UpdateProjectLength();
            RebuildMixerTracks();
            LayoutAll();
            pnlTrackSurface.Invalidate();
        }

        private void EditSelectedClip()
        {
            if (selectedClipIndices.Count != 1)
            {
                MessageBox.Show("Seleziona una sola traccia per modificarla.", "Modifica", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var clip = clips[selectedClipIndices[0]];
            var editor = new SampleEditForm(clip);
            if (editor.ShowDialog() == DialogResult.OK)
            {
                clips[selectedClipIndices[0]] = editor.ModifiedClip;
                BuildWaveformPeaksAsync(editor.ModifiedClip, 2500);
                UpdateProjectLength();
                RebuildMixerTracks();
                pnlTrackSurface.Invalidate();
            }
        }

        private void SplitSelectedClip()
        {
            if (selectedClipIndices.Count != 1)
            {
                MessageBox.Show("Seleziona una sola traccia da separare.", "Separa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var clip = clips[selectedClipIndices[0]];
            float splitTime = playheadSec;

            if (splitTime <= clip.StartSec || splitTime >= clip.StartSec + clip.DurationSec)
            {
                MessageBox.Show($"Posiziona il playhead ({splitTime:F2}s) all'interno del clip ({clip.StartSec:F2} - {clip.StartSec + clip.DurationSec:F2}).", "Separa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            float offset = splitTime - clip.StartSec;
            string temp1 = CreateTempWavFromClip(clip, 0, offset);
            string temp2 = CreateTempWavFromClip(clip, offset, clip.DurationSec - offset);

            if (temp1 == null || temp2 == null) return;

            var newClip1 = new AudioClipInfo
            {
                FilePath = temp1,
                DisplayName = clip.DisplayName + "_part1",
                StartSec = clip.StartSec,
                DurationSec = offset,
                LaneIndex = clip.LaneIndex,
                Volume = clip.Volume
            };
            var newClip2 = new AudioClipInfo
            {
                FilePath = temp2,
                DisplayName = clip.DisplayName + "_part2",
                StartSec = splitTime,
                DurationSec = clip.DurationSec - offset,
                LaneIndex = clip.LaneIndex,
                Volume = clip.Volume
            };

            int idx = selectedClipIndices[0];
            clips.RemoveAt(idx);
            clips.Insert(idx, newClip2);
            clips.Insert(idx, newClip1);

            selectedClipIndices.Clear();
            BuildWaveformPeaksAsync(newClip1, 2500);
            BuildWaveformPeaksAsync(newClip2, 2500);
            UpdateProjectLength();
            RebuildMixerTracks();
            pnlTrackSurface.Invalidate();
        }

        private void MergeSelectedClips()
        {
            if (selectedClipIndices.Count < 2)
            {
                MessageBox.Show("Seleziona almeno due tracce da unire.", "Unisci", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var clipsToMerge = selectedClipIndices.OrderBy(i => clips[i].StartSec).Select(i => clips[i]).ToList();
            float totalStart = clipsToMerge.Min(c => c.StartSec);
            float totalEnd = clipsToMerge.Max(c => c.StartSec + c.DurationSec);
            float totalDuration = totalEnd - totalStart;

            string mergedFile = Path.GetTempFileName() + ".wav";
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));

            foreach (var c in clipsToMerge)
            {
                var reader = new AudioFileReader(c.FilePath) { Volume = c.Volume };
                var offset = new OffsetSampleProvider(reader)
                {
                    DelayBy = TimeSpan.FromSeconds(c.StartSec - totalStart),
                    Take = TimeSpan.FromSeconds(c.DurationSec)
                };
                mixer.AddMixerInput(offset);
            }

            using (var writer = new WaveFileWriter(mergedFile, mixer.WaveFormat))
            {
                float[] buffer = new float[8192];
                long neededSamples = (long)(totalDuration * 44100 * 2);
                long written = 0;
                while (written < neededSamples)
                {
                    int toRead = (int)Math.Min(buffer.Length, neededSamples - written);
                    int read = mixer.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    writer.WriteSamples(buffer, 0, read);
                    written += read;
                }
            }

            var mergedClip = new AudioClipInfo
            {
                FilePath = mergedFile,
                DisplayName = "Merged_" + DateTime.Now.ToString("HHmmss"),
                StartSec = totalStart,
                DurationSec = totalDuration,
                LaneIndex = clipsToMerge[0].LaneIndex,
                Volume = 1f
            };

            foreach (int idx in selectedClipIndices.OrderByDescending(i => i))
                clips.RemoveAt(idx);

            clips.Add(mergedClip);
            BuildWaveformPeaksAsync(mergedClip, 2500);
            selectedClipIndices.Clear();
            UpdateProjectLength();
            RebuildMixerTracks();
            LayoutAll();
            pnlTrackSurface.Invalidate();
        }

        private AudioClipInfo CloneClip(AudioClipInfo original)
        {
            return new AudioClipInfo
            {
                FilePath = original.FilePath,
                DisplayName = original.DisplayName,
                StartSec = original.StartSec,
                DurationSec = original.DurationSec,
                LaneIndex = original.LaneIndex,
                Volume = original.Volume,
                IsLoopClip = original.IsLoopClip,
                LoopLengthSec = original.LoopLengthSec,
                PianoNotes = original.PianoNotes?.ToList(),
                TotalSteps = original.TotalSteps,
                StepLengthSec = original.StepLengthSec,
                IsDrumKit = original.IsDrumKit
            };
        }

        private string CreateTempWavFromClip(AudioClipInfo clip, float startSec, float durationSec)
        {
            try
            {
                string temp = Path.GetTempFileName() + ".wav";
                using (var reader = new AudioFileReader(clip.FilePath))
                {
                    reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                    var take = new OffsetSampleProvider(reader) { Take = TimeSpan.FromSeconds(durationSec) };
                    WaveFileWriter.CreateWaveFile16(temp, take);
                }
                return temp;
            }
            catch
            {
                return null;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { timerPlay?.Stop(); try { waveSource?.StopRecording(); } catch { } waveFile?.Dispose(); waveSource?.Dispose(); StopPlaybackInternal(false); base.OnFormClosing(e); }
    }

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { var rect = new Rectangle(Point.Empty, e.Item.Size); var color = e.Item.Selected ? Color.FromArgb(65, 70, 90) : Color.FromArgb(45, 49, 58); using var brush = new SolidBrush(color); e.Graphics.FillRectangle(brush, rect); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.White; base.OnRenderItemText(e); }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(60, 65, 80); public override Color MenuItemBorder => Color.FromArgb(65, 70, 90); public override Color MenuItemSelected => Color.FromArgb(65, 70, 90); public override Color MenuItemSelectedGradientBegin => Color.FromArgb(65, 70, 90); public override Color MenuItemSelectedGradientEnd => Color.FromArgb(65, 70, 90); public override Color MenuStripGradientBegin => Color.FromArgb(45, 49, 58); public override Color MenuStripGradientEnd => Color.FromArgb(45, 49, 58); public override Color ToolStripDropDownBackground => Color.FromArgb(45, 49, 58); public override Color ImageMarginGradientBegin => Color.FromArgb(45, 49, 58); public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 49, 58); public override Color ImageMarginGradientEnd => Color.FromArgb(45, 49, 58);
    }
}