using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ProgettoBattaglino
{
    public class SequencerNote
    {
        public int Step { get; set; }
        public int Length { get; set; }
        public string Note { get; set; }
    }

    public class InstrumentForm : Form
    {
        private static readonly string[] NoteNames = new[]
        {
            "C5","B4","A#4","A4","G#4","G4","F#4","F4","E4","D#4","D4","C#4","C4",
            "B3","A#3","A3","G#3","G3","F#3","F3","E3","D#3","D3","C#3","C3"
        };

        private int currentSteps = 16;
        private const int CellW = 48;
        private const int CellH = 26;
        private const int PianoW = 64;
        private const int HeaderH = 32;
        private const int MixerRate = 44100;
        private const int MixerCh = 2;

        private readonly string instrumentName;
        private readonly string samplePath;

        private readonly List<SequencerNote> activeNotes = new List<SequencerNote>();

        private WaveOutEvent sequencerPlayer;
        private MixingSampleProvider sequencerMixer;
        private Timer playTimer;
        private int playStep = 0;
        private int bpm = 120;
        private bool isPlaying = false;

        private BufferedPanel pnlGrid;
        private Button btnPlay;
        private Button btnStop;
        private Button btnAddMelody;
        private CheckBox chkPedal;
        private TrackBar trkBpm;
        private Label lblBpm;

        private SequencerNote draggingNote = null;
        private bool isErasing = false;

        public InstrumentForm(string instrumentName, string samplePath)
        {
            this.instrumentName = instrumentName;
            this.samplePath = samplePath;

            InitForm();
        }

        private void InitForm()
        {
            Text = $"Piano Roll — {instrumentName}";
            BackColor = Color.FromArgb(28, 30, 36);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 9);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(38, 41, 50) };
            Controls.Add(toolbar);

            btnPlay = MakeBtn("▶  Play", 10, 10, 80);
            btnStop = MakeBtn("■  Stop", 98, 10, 80);
            btnAddMelody = MakeBtn("Aggiungi melodia al DAW", 200, 10, 180);
            btnPlay.Click += (s, e) => StartSequencer();
            btnStop.Click += (s, e) => StopSequencer();
            btnAddMelody.Click += BtnAddMelody_Click;

            toolbar.Controls.Add(btnPlay);
            toolbar.Controls.Add(btnStop);
            toolbar.Controls.Add(btnAddMelody);

            chkPedal = new CheckBox { Text = "Pedale Sustain", Checked = true, ForeColor = Color.Gainsboro, BackColor = Color.Transparent, Location = new Point(400, 15), AutoSize = true };
            toolbar.Controls.Add(chkPedal);

            lblBpm = new Label { Text = $"BPM: {bpm}", ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(580, 15) };
            toolbar.Controls.Add(lblBpm);

            trkBpm = new TrackBar { Minimum = 40, Maximum = 240, Value = bpm, TickStyle = TickStyle.None, Width = 140, Height = 30, Location = new Point(640, 10) };
            trkBpm.Scroll += (s, e) => { bpm = trkBpm.Value; lblBpm.Text = $"BPM: {bpm}"; };
            toolbar.Controls.Add(trkBpm);

            var btnClear = MakeBtn("Cancella tutto", ClientSize.Width - 140, 10, 120);
            btnClear.Click += (s, e) => { activeNotes.Clear(); pnlGrid.Invalidate(); };
            toolbar.Controls.Add(btnClear);

            pnlGrid = new BufferedPanel { Location = new Point(10, 56), BackColor = Color.FromArgb(24, 26, 31) };
            pnlGrid.Paint += PnlGrid_Paint;
            pnlGrid.MouseDown += PnlGrid_MouseDown;
            pnlGrid.MouseMove += PnlGrid_MouseMove;
            pnlGrid.MouseUp += PnlGrid_MouseUp;
            Controls.Add(pnlGrid);

            playTimer = new Timer { Interval = 30 };
            playTimer.Tick += PlayTimer_Tick;

            Resize += (s, e) => RecalculateGrid();
            RecalculateGrid();

            FormClosed += (s, e) => { StopSequencer(); };
        }

        private Button MakeBtn(string text, int x, int y, int w)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 60, 72), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void RecalculateGrid()
        {
            int availableWidth = ClientSize.Width - 40;
            currentSteps = Math.Max(8, (availableWidth - PianoW) / CellW);
            int gridW = PianoW + currentSteps * CellW + 2;
            int gridH = HeaderH + NoteNames.Length * CellH + 2;

            pnlGrid.Size = new Size(gridW, gridH);
            pnlGrid.Location = new Point(10, 56);

            foreach (Control c in Controls[0].Controls)
                if (c is Button btn && btn.Text == "Cancella tutto")
                    btn.Location = new Point(gridW - 140, 10);

            pnlGrid.Invalidate();
        }

        private void PnlGrid_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(24, 26, 31));

            using var stepFont = new Font("Segoe UI", 8);
            using var stepBrush = new SolidBrush(Color.FromArgb(140, 148, 168));
            for (int s = 0; s < currentSteps; s++)
            {
                int x = PianoW + s * CellW;
                bool active = isPlaying && s == playStep;
                using var hb = new SolidBrush(active ? Color.FromArgb(80, 100, 160) : (s % 4 == 0 ? Color.FromArgb(42, 46, 58) : Color.FromArgb(35, 38, 47)));
                g.FillRectangle(hb, x, 0, CellW, HeaderH);
                g.DrawString((s + 1).ToString(), stepFont, stepBrush, x + 4, 8);
            }

            for (int ni = 0; ni < NoteNames.Length; ni++)
            {
                string note = NoteNames[ni];
                int y = HeaderH + ni * CellH;
                bool isC = note.StartsWith("C") && !note.Contains("#");
                bool isBlack = note.Contains("#");
                Color rowBg = isC ? Color.FromArgb(32, 36, 50) : (isBlack ? Color.FromArgb(22, 24, 32) : Color.FromArgb(28, 31, 40));

                for (int s = 0; s < currentSteps; s++)
                {
                    int x = PianoW + s * CellW;
                    Color cellBg = (s % 4 == 0) ? Color.FromArgb(rowBg.R + 6, rowBg.G + 6, rowBg.B + 10) : rowBg;
                    using var cellBrush = new SolidBrush(cellBg);
                    g.FillRectangle(cellBrush, x + 1, y + 1, CellW - 1, CellH - 1);
                }

                using var pianoBrush = new SolidBrush(isBlack ? Color.FromArgb(35, 35, 42) : Color.FromArgb(220, 222, 230));
                g.FillRectangle(pianoBrush, 1, y + 1, PianoW - 2, CellH - 1);
                using var pianoTxt = new SolidBrush(isBlack ? Color.FromArgb(180, 185, 200) : Color.FromArgb(50, 50, 60));
                using var noteFont = new Font("Segoe UI", 8, isC ? FontStyle.Bold : FontStyle.Regular);
                g.DrawString(note, noteFont, pianoTxt, 4, y + CellH / 2 - 7);

                using var sep = new Pen(Color.FromArgb(40, 44, 58), 1);
                g.DrawLine(sep, 0, y, PianoW + currentSteps * CellW, y);
            }

            for (int s = 0; s <= currentSteps; s++)
            {
                int x = PianoW + s * CellW;
                using var vp = new Pen(s % 4 == 0 ? Color.FromArgb(60, 66, 84) : Color.FromArgb(38, 42, 54), 1);
                g.DrawLine(vp, x, 0, x, HeaderH + NoteNames.Length * CellH);
            }

            foreach (var n in activeNotes)
            {
                int ni = Array.IndexOf(NoteNames, n.Note);
                if (ni < 0) continue;

                int x = PianoW + n.Step * CellW;
                int y = HeaderH + ni * CellH;
                int w = n.Length * CellW;

                bool isBlack = n.Note.Contains("#");
                Color cellBg = isBlack ? Color.FromArgb(60, 100, 200) : Color.FromArgb(75, 120, 220);

                if (isPlaying && playStep >= n.Step && playStep < n.Step + n.Length)
                    cellBg = Color.FromArgb(120, 190, 255);

                using var cellBrush = new SolidBrush(cellBg);
                g.FillRectangle(cellBrush, x + 1, y + 1, w - 2, CellH - 2);

                using var border = new Pen(Color.FromArgb(150, 200, 255), 2);
                g.DrawRectangle(border, x + 1, y + 1, w - 3, CellH - 3);
            }

            if (isPlaying)
            {
                int playheadX = PianoW + playStep * CellW;
                using var pHead = new Pen(Color.FromArgb(220, 50, 50), 2);
                g.DrawLine(pHead, playheadX, HeaderH, playheadX, pnlGrid.Height);
            }

            using var pianoSep = new Pen(Color.FromArgb(60, 66, 84), 2);
            g.DrawLine(pianoSep, PianoW, 0, PianoW, HeaderH + NoteNames.Length * CellH);
        }

        private void PnlGrid_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.X < PianoW) return;
            int s = (e.X - PianoW) / CellW; int ni = (e.Y - HeaderH) / CellH;
            if (s < 0 || s >= currentSteps || ni < 0 || ni >= NoteNames.Length) return;

            string note = NoteNames[ni];
            var existing = activeNotes.FirstOrDefault(n => n.Note == note && s >= n.Step && s < n.Step + n.Length);

            if (existing != null)
            {
                activeNotes.Remove(existing);
                isErasing = true;
            }
            else
            {
                draggingNote = new SequencerNote { Step = s, Length = 1, Note = note };
                activeNotes.Add(draggingNote);
                isErasing = false;
                PlayNotePreview(note);
            }
            pnlGrid.Invalidate();
        }

        private void PnlGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && draggingNote != null && !isErasing)
            {
                int s = (e.X - PianoW) / CellW;
                if (s >= draggingNote.Step && s < currentSteps)
                {
                    int newLen = s - draggingNote.Step + 1;
                    if (newLen != draggingNote.Length)
                    {
                        draggingNote.Length = newLen;
                        pnlGrid.Invalidate();
                    }
                }
            }
        }

        private void PnlGrid_MouseUp(object sender, MouseEventArgs e)
        {
            draggingNote = null;
            isErasing = false;
        }

        private void StartSequencer()
        {
            StopSequencer(); // Ferma in sicurezza tutto quello di attivo 

            isPlaying = true;
            playStep = 0;

            sequencerMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixerRate, MixerCh)) { ReadFully = true };
            sequencerPlayer = new WaveOutEvent();
            sequencerPlayer.Init(sequencerMixer);
            sequencerPlayer.Play();

            playTimer.Interval = Math.Max(30, 60000 / bpm / 4);
            playTimer.Start();

            btnPlay.BackColor = Color.FromArgb(40, 80, 140);
            pnlGrid.Invalidate();
        }

        private void StopSequencer()
        {
            isPlaying = false;
            playTimer.Stop();

            if (sequencerPlayer != null)
            {
                try { sequencerPlayer.Stop(); sequencerPlayer.Dispose(); } catch { }
                sequencerPlayer = null;
            }

            sequencerMixer = null;
            playStep = 0;
            btnPlay.BackColor = Color.FromArgb(55, 60, 72);
            if (pnlGrid != null) pnlGrid.Invalidate();
        }

        private void PlayTimer_Tick(object sender, EventArgs e)
        {
            playTimer.Interval = Math.Max(30, 60000 / bpm / 4);

            var notesThisStep = activeNotes.Where(k => k.Step == playStep).ToList();
            if (notesThisStep.Count > 0)
            {
                float stepSec = 60f / bpm / 4f;
                foreach (var note in notesThisStep)
                {
                    var pitched = BuildPitchedSample(note.Note);
                    if (pitched != null)
                    {
                        var offset = new OffsetSampleProvider(pitched)
                        {
                            DelayBy = TimeSpan.Zero,
                            Take = TimeSpan.FromSeconds(note.Length * stepSec)
                        };
                        sequencerMixer.AddMixerInput(offset);
                    }
                }
            }

            playStep = (playStep + 1) % currentSteps;
            pnlGrid.Invalidate();
        }

        private void BtnAddMelody_Click(object sender, EventArgs e)
        {
            if (activeNotes.Count == 0) { MessageBox.Show("Nessuna nota nel sequencer!", "Attenzione", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            StopSequencer();
            Application.DoEvents();

            float stepSec = 60f / bpm / 4f;

            int lastStepInvolved = activeNotes.Count > 0 ? activeNotes.Max(n => n.Step + n.Length - 1) : 0;
            int neededSteps = ((lastStepInvolved / 4) + 1) * 4;
            float patternDuration = neededSteps * stepSec;

            string tempWav = Path.Combine(Path.GetTempPath(), $"Melodia_{instrumentName}_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            try
            {
                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixerRate, MixerCh)) { ReadFully = false };
                foreach (var entry in activeNotes)
                {
                    var pitched = BuildPitchedSample(entry.Note);
                    if (pitched != null)
                    {
                        var offset = new OffsetSampleProvider(pitched)
                        {
                            DelayBy = TimeSpan.FromSeconds(entry.Step * stepSec),
                            Take = TimeSpan.FromSeconds(entry.Length * stepSec)
                        };
                        mixer.AddMixerInput(offset);
                    }
                }

                using (var writer = new WaveFileWriter(tempWav, mixer.WaveFormat))
                {
                    float[] buffer = new float[4096];
                    long totalSamplesToGenerate = (long)(patternDuration * MixerRate * MixerCh);
                    long samplesWritten = 0;

                    while (samplesWritten < totalSamplesToGenerate)
                    {
                        int toRead = (int)Math.Min(buffer.Length, totalSamplesToGenerate - samplesWritten);
                        int read = mixer.Read(buffer, 0, toRead);

                        if (read < toRead)
                        {
                            Array.Clear(buffer, read, toRead - read);
                            read = toRead;
                        }

                        writer.WriteSamples(buffer, 0, read);
                        samplesWritten += read;
                    }
                }

                // Invio alla form principale passando anche l'attributo stepSec vitale per lo snap
                if (Owner is Form1 mainForm)
                    mainForm.AddMelodyClip(tempWav, patternDuration, activeNotes, neededSteps, stepSec);

                MessageBox.Show($"Melodia esportata correttamente!\nDurata: {patternDuration:F2}s ({neededSteps} step)", "Successo");
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show("Errore:\n" + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static readonly Dictionary<string, int> NoteToSemitone = BuildNoteMap();
        private static Dictionary<string, int> BuildNoteMap()
        {
            var chromatic = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var map = new Dictionary<string, int>();
            for (int oct = 2; oct <= 6; oct++) for (int i = 0; i < chromatic.Length; i++) map[chromatic[i] + oct] = (oct - 4) * 12 + i;
            return map;
        }

        private static double PitchFactor(string note) => NoteToSemitone.TryGetValue(note, out int semi) ? Math.Pow(2.0, semi / 12.0) : 1.0;

        private void PlayNotePreview(string note)
        {
            if (!File.Exists(samplePath)) return;
            try { var samples = BuildPitchedSample(note); if (samples == null) return; var preview = new WaveOutEvent(); preview.Init(samples); preview.PlaybackStopped += (s, e2) => preview.Dispose(); preview.Play(); } catch { }
        }

        private ISampleProvider BuildPitchedSample(string note)
        {
            try
            {
                var reader = new AudioFileReader(samplePath);
                ISampleProvider stereo = reader.WaveFormat.Channels == 1 ? new MonoToStereoSampleProvider(reader) : (ISampleProvider)reader;
                double factor = PitchFactor(note);
                int virtualSR = Math.Max(8000, Math.Min(192000, (int)Math.Round(reader.WaveFormat.SampleRate * factor)));
                return new AutoDisposeSampleProvider(new WdlResamplingSampleProvider(new FakeSampleRateProvider(stereo, virtualSR), MixerRate), reader);
            }
            catch { return null; }
        }
    }

    internal class AutoDisposeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly IDisposable resourceToDispose;
        private bool isDisposed;

        public AutoDisposeSampleProvider(ISampleProvider source, IDisposable resourceToDispose) { this.source = source; this.resourceToDispose = resourceToDispose; }
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            if (isDisposed) return 0;
            int read = source.Read(buffer, offset, count);
            if (read == 0) { resourceToDispose?.Dispose(); isDisposed = true; }
            return read;
        }
    }

    internal class FakeSampleRateProvider : ISampleProvider
    {
        private readonly ISampleProvider source; private readonly WaveFormat fakeFormat;
        public FakeSampleRateProvider(ISampleProvider source, int fakeSampleRate) { this.source = source; fakeFormat = WaveFormat.CreateIeeeFloatWaveFormat(fakeSampleRate, source.WaveFormat.Channels); }
        public WaveFormat WaveFormat => fakeFormat;
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    }
}