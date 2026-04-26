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
    public class DrumKitForm : Form
    {
        private static readonly string[] DrumPieces = new[]
        {
            "Crash", "Ride", "HiHat", "Tom", "Snare", "Kick"
        };

        private int currentSteps = 16;
        private const int CellW = 48;
        private const int CellH = 26;
        private const int PianoW = 80; // Più largo per accogliere comodamente le scritte
        private const int HeaderH = 32;
        private const int MixerRate = 44100;
        private const int MixerCh = 2;

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
        private TrackBar trkBpm;
        private Label lblBpm;

        private SequencerNote draggingNote = null;
        private bool isErasing = false;

        public DrumKitForm()
        {
            InitForm();
        }

        private void InitForm()
        {
            Text = "Drum Kit — Batteria";
            BackColor = Color.FromArgb(28, 30, 36);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 9);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            MinimumSize = new Size(600, 300);
            StartPosition = FormStartPosition.CenterParent;

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(38, 41, 50) };
            Controls.Add(toolbar);

            btnPlay = MakeBtn("▶  Play", 10, 10, 80);
            btnStop = MakeBtn("■  Stop", 98, 10, 80);
            btnAddMelody = MakeBtn("Aggiungi batteria al DAW", 200, 10, 180);
            btnPlay.Click += (s, e) => StartSequencer();
            btnStop.Click += (s, e) => StopSequencer();
            btnAddMelody.Click += BtnAddMelody_Click;

            toolbar.Controls.Add(btnPlay);
            toolbar.Controls.Add(btnStop);
            toolbar.Controls.Add(btnAddMelody);

            lblBpm = new Label { Text = $"BPM: {bpm}", ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(400, 15) };
            toolbar.Controls.Add(lblBpm);

            trkBpm = new TrackBar { Minimum = 40, Maximum = 240, Value = bpm, TickStyle = TickStyle.None, Width = 140, Height = 30, Location = new Point(450, 10) };
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
            int gridH = HeaderH + DrumPieces.Length * CellH + 2;

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

            for (int ni = 0; ni < DrumPieces.Length; ni++)
            {
                string piece = DrumPieces[ni];
                int y = HeaderH + ni * CellH;
                Color rowBg = (ni % 2 == 0) ? Color.FromArgb(32, 36, 50) : Color.FromArgb(28, 31, 40);

                for (int s = 0; s < currentSteps; s++)
                {
                    int x = PianoW + s * CellW;
                    Color cellBg = (s % 4 == 0) ? Color.FromArgb(rowBg.R + 6, rowBg.G + 6, rowBg.B + 10) : rowBg;
                    using var cellBrush = new SolidBrush(cellBg);
                    g.FillRectangle(cellBrush, x + 1, y + 1, CellW - 1, CellH - 1);
                }

                using var pianoBrush = new SolidBrush(Color.FromArgb(45, 45, 55));
                g.FillRectangle(pianoBrush, 1, y + 1, PianoW - 2, CellH - 1);
                using var pianoTxt = new SolidBrush(Color.FromArgb(200, 200, 200));
                using var noteFont = new Font("Segoe UI", 8, FontStyle.Bold);
                g.DrawString(piece, noteFont, pianoTxt, 4, y + CellH / 2 - 7);

                using var sep = new Pen(Color.FromArgb(40, 44, 58), 1);
                g.DrawLine(sep, 0, y, PianoW + currentSteps * CellW, y);
            }

            for (int s = 0; s <= currentSteps; s++)
            {
                int x = PianoW + s * CellW;
                using var vp = new Pen(s % 4 == 0 ? Color.FromArgb(60, 66, 84) : Color.FromArgb(38, 42, 54), 1);
                g.DrawLine(vp, x, 0, x, HeaderH + DrumPieces.Length * CellH);
            }

            foreach (var n in activeNotes)
            {
                int ni = Array.IndexOf(DrumPieces, n.Note);
                if (ni < 0) continue;

                int x = PianoW + n.Step * CellW;
                int y = HeaderH + ni * CellH;
                int w = n.Length * CellW;

                Color cellBg = Color.FromArgb(200, 100, 50);

                if (isPlaying && playStep >= n.Step && playStep < n.Step + n.Length)
                    cellBg = Color.FromArgb(255, 140, 80);

                using var cellBrush = new SolidBrush(cellBg);
                g.FillRectangle(cellBrush, x + 1, y + 1, w - 2, CellH - 2);

                using var border = new Pen(Color.FromArgb(255, 180, 100), 2);
                g.DrawRectangle(border, x + 1, y + 1, w - 3, CellH - 3);
            }

            if (isPlaying)
            {
                int playheadX = PianoW + playStep * CellW;
                using var pHead = new Pen(Color.FromArgb(220, 50, 50), 2);
                g.DrawLine(pHead, playheadX, HeaderH, playheadX, pnlGrid.Height);
            }

            using var pianoSep = new Pen(Color.FromArgb(60, 66, 84), 2);
            g.DrawLine(pianoSep, PianoW, 0, PianoW, HeaderH + DrumPieces.Length * CellH);
        }

        private void PnlGrid_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.X < PianoW) return;
            int s = (e.X - PianoW) / CellW; int ni = (e.Y - HeaderH) / CellH;
            if (s < 0 || s >= currentSteps || ni < 0 || ni >= DrumPieces.Length) return;

            string note = DrumPieces[ni];
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
                PlayDrumPreview(note);
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
            StopSequencer();

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
                    var sample = BuildDrumSample(note.Note);
                    if (sample != null)
                    {
                        var offset = new OffsetSampleProvider(sample)
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
            if (activeNotes.Count == 0) { MessageBox.Show("Nessun pezzo nel DrumKit!", "Attenzione", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            StopSequencer();
            Application.DoEvents();

            float stepSec = 60f / bpm / 4f;

            int lastStepInvolved = activeNotes.Count > 0 ? activeNotes.Max(n => n.Step + n.Length - 1) : 0;
            int neededSteps = ((lastStepInvolved / 4) + 1) * 4;
            float patternDuration = neededSteps * stepSec;

            string tempWav = Path.Combine(Path.GetTempPath(), $"Batteria_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            try
            {
                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixerRate, MixerCh)) { ReadFully = false };
                foreach (var entry in activeNotes)
                {
                    var sample = BuildDrumSample(entry.Note);
                    if (sample != null)
                    {
                        var offset = new OffsetSampleProvider(sample)
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

                if (Owner is Form1 mainForm)
                    mainForm.AddMelodyClip(tempWav, patternDuration, activeNotes, neededSteps, stepSec, true);

                MessageBox.Show($"Batteria esportata correttamente!\nDurata: {patternDuration:F2}s ({neededSteps} step)", "Successo");
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show("Errore:\n" + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void PlayDrumPreview(string piece)
        {
            try
            {
                var samples = BuildDrumSample(piece);
                if (samples == null) return;
                var preview = new WaveOutEvent();
                preview.Init(samples);
                preview.PlaybackStopped += (s, e2) => preview.Dispose();
                preview.Play();
            }
            catch { }
        }

        private ISampleProvider BuildDrumSample(string piece)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Strumenti", "Batteria", $"{piece}.wav");

            // 1. Controllo se il file esiste fisicamente
            if (!File.Exists(path))
            {
                MessageBox.Show($"File non trovato: {path}\nAssicurati di averlo chiamato esattamente '{piece}.wav'", "Manca file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            try
            {
                // 2. Lettura standard (funziona con 16-bit PCM)
                var reader = new AudioFileReader(path);
                ISampleProvider stereo = reader.WaveFormat.Channels == 1 ? new MonoToStereoSampleProvider(reader) : (ISampleProvider)reader;
                ISampleProvider resampled = reader.WaveFormat.SampleRate == MixerRate ? stereo : new WdlResamplingSampleProvider(stereo, MixerRate);

                return new AutoDisposeSampleProvider(resampled, reader);
            }
            catch
            {
                try
                {
                    // 3. Fallback d'emergenza per WAV a 24/32-bit e compressi
                    var mfReader = new MediaFoundationReader(path);
                    var sampleProvider = mfReader.ToSampleProvider();

                    ISampleProvider stereo = sampleProvider.WaveFormat.Channels == 1 ? new MonoToStereoSampleProvider(sampleProvider) : sampleProvider;
                    ISampleProvider resampled = sampleProvider.WaveFormat.SampleRate == MixerRate ? stereo : new WdlResamplingSampleProvider(stereo, MixerRate);

                    return new AutoDisposeSampleProvider(resampled, mfReader);
                }
                catch (Exception ex)
                {
                    // 4. Mostra il vero errore
                    MessageBox.Show($"Impossibile leggere l'audio di {piece}.wav!\nMotivo: {ex.Message}\n\nProva a convertire il file in WAV 16-bit.", "Errore Formato Audio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }
        }
    }
}