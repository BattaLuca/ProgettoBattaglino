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
    public partial class Form1 : Form
    {
        // =========================
        //  LAYOUT UI (DAW)
        // =========================
        private Panel pnlTopBar;
        private Panel pnlLeft;
        private Panel pnlCenter;
        private Panel pnlBottomMixer;

        // Timeline / Tracks
        private Panel pnlRuler;
        private Panel pnlTrackSurface;

        private VScrollBar vScroll;
        private HScrollBar hScroll;

        // Mixer
        private FlowLayoutPanel pnlMixerTracks;

        // Timeline view
        private int pixelsPerSecond = 70;
        private int timelineOffsetX = 0;
        private int rowHeight = 70;
        private const int HeaderW = 170;
        private float timelineSeconds = 10f;

        // Playhead
        private float playheadSec = 0f;
        private bool showPlayhead = false;

        // Transport buttons
        private Button btnPlay;
        private Button btnStop;
        private Button btnRec;

        // =========================
        //  NAudio: RECORD
        // =========================
        private bool isRecording = false;
        private WaveInEvent waveSource;
        private WaveFileWriter waveFile;
        private string lastRecord;

        // =========================
        //  NAudio: PLAYBACK MULTI-TRACK
        // =========================
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

        // =========================
        //  CLIP / TRACK INFO
        // =========================
        private class AudioClipInfo
        {
            public string FilePath { get; set; }
            public string DisplayName { get; set; }
            public float StartSec { get; set; }
            public float DurationSec { get; set; }
            public int LaneIndex { get; set; }
            public float Volume { get; set; } = 1f;
            public List<float> Peaks { get; set; } = new List<float>();

            // Reader attivo durante il playback
            public AudioFileReader ActiveReader { get; set; }
        }

        private readonly List<AudioClipInfo> clips = new List<AudioClipInfo>();
        private int selectedClipIndex = -1;

        public Form1()
        {
            InitializeComponent();
            BuildUI();
            LayoutAll();
        }

        // =========================
        //  BUILD UI
        // =========================
        private void BuildUI()
        {
            Text = "ProgettoBattaglino - DAW";
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(30, 33, 38);
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            // TOP BAR
            pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(45, 49, 58)
            };
            Controls.Add(pnlTopBar);
            BuildTopBar();

            // BOTTOM MIXER
            pnlBottomMixer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 220,
                BackColor = Color.FromArgb(35, 38, 46)
            };
            Controls.Add(pnlBottomMixer);
            BuildMixer();

            // LEFT PANEL
            pnlLeft = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            Controls.Add(pnlLeft);
            BuildLeftPanel();

            // CENTER
            pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 36)
            };
            Controls.Add(pnlCenter);

            // RULER
            pnlRuler = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(38, 41, 50)
            };
            pnlRuler.Paint += PnlRuler_Paint;
            pnlRuler.MouseDown += Timeline_MouseDownSeek;
            pnlCenter.Controls.Add(pnlRuler);

            // TRACK SURFACE
            pnlTrackSurface = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 26, 31),
                TabStop = true
            };
            pnlTrackSurface.Paint += PnlTrackSurface_Paint;
            pnlTrackSurface.MouseDown += Timeline_MouseDownSeek;
            pnlTrackSurface.MouseMove += PnlTrackSurface_MouseMove;
            pnlTrackSurface.MouseUp += PnlTrackSurface_MouseUp;
            pnlCenter.Controls.Add(pnlTrackSurface);

            // Scrollbars
            vScroll = new VScrollBar
            {
                Dock = DockStyle.Right,
                Width = 16
            };
            vScroll.Scroll += (s, e) => pnlTrackSurface.Invalidate();
            pnlTrackSurface.Controls.Add(vScroll);

            hScroll = new HScrollBar
            {
                Dock = DockStyle.Bottom,
                Height = 16
            };
            hScroll.Scroll += (s, e) =>
            {
                timelineOffsetX = hScroll.Value;
                pnlRuler.Invalidate();
                pnlTrackSurface.Invalidate();
            };
            pnlCenter.Controls.Add(hScroll);

            // Timer playhead
            timerPlay = new Timer { Interval = 30 };
            timerPlay.Tick += timerPlay_Tick;

            Resize += (s, e) => LayoutAll();
        }

        private void BuildTopBar()
        {
            var lblTitle = new Label
            {
                Text = "MyDAW - C# WinForms + NAudio",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(12, 12)
            };
            pnlTopBar.Controls.Add(lblTitle);

            btnPlay = MakeTopButton("▶");
            btnStop = MakeTopButton("■");
            btnRec = MakeTopButton("●");
            btnRec.ForeColor = Color.IndianRed;

            btnPlay.Location = new Point(300, 8);
            btnStop.Location = new Point(340, 8);
            btnRec.Location = new Point(380, 8);

            pnlTopBar.Controls.Add(btnPlay);
            pnlTopBar.Controls.Add(btnStop);
            pnlTopBar.Controls.Add(btnRec);

            btnPlay.Click += (s, e) => StartPlayback();
            btnStop.Click += (s, e) => StopAndReturnToStart();
            btnRec.Click += (s, e) => ToggleRecord();
        }

        private Button MakeTopButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Width = 34,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 60, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildLeftPanel()
        {
            var gbBrowser = new GroupBox
            {
                Text = "File Browser",
                Dock = DockStyle.Top,
                Height = 270,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            pnlLeft.Controls.Add(gbBrowser);

            var tv = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(26, 28, 34),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None
            };
            tv.Nodes.Add("Desktop");
            tv.Nodes.Add("Documents");
            tv.Nodes.Add("Music");
            gbBrowser.Controls.Add(tv);

            var gbPlugins = new GroupBox
            {
                Text = "Instrument Plugins",
                Dock = DockStyle.Top,
                Height = 240,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            pnlLeft.Controls.Add(gbPlugins);

            var lst = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(26, 28, 34),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None
            };
            lst.Items.AddRange(new object[]
            {
                "Audio Plugin",
                "Piano Plugin",
                "Drum Plugin",
                "Synth Plugin",
                "Reverb Plugin",
                "Rustico Plugin"
            });
            gbPlugins.Controls.Add(lst);

            pnlLeft.Controls.Add(new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 37, 45)
            });
        }

        // =========================
        //  MIXER
        // =========================
        private void BuildMixer()
        {
            pnlMixerTracks = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(35, 38, 46)
            };

            pnlBottomMixer.Controls.Add(pnlMixerTracks);
            RebuildMixerTracks();
        }

        private void RebuildMixerTracks()
        {
            pnlMixerTracks.SuspendLayout();
            pnlMixerTracks.Controls.Clear();

            if (clips.Count == 0)
            {
                var lblEmpty = new Label
                {
                    Text = "Nessuna traccia registrata",
                    ForeColor = Color.Gainsboro,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    Margin = new Padding(10)
                };
                pnlMixerTracks.Controls.Add(lblEmpty);
                pnlMixerTracks.ResumeLayout();
                return;
            }

            for (int i = 0; i < clips.Count; i++)
            {
                int clipIndex = i;
                AudioClipInfo clip = clips[clipIndex];

                var strip = new Panel
                {
                    Width = 120,
                    Height = 180,
                    BackColor = clipIndex == selectedClipIndex
                        ? Color.FromArgb(55, 65, 95)
                        : Color.FromArgb(30, 33, 40),
                    Margin = new Padding(0, 0, 10, 0),
                    Tag = clipIndex
                };

                var lblTrack = new Label
                {
                    Text = $"TRACK {clipIndex + 1}",
                    ForeColor = Color.Gainsboro,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    Location = new Point(10, 10),
                    Tag = clipIndex
                };
                strip.Controls.Add(lblTrack);

                var lblName = new Label
                {
                    Text = clip.DisplayName,
                    ForeColor = Color.Silver,
                    AutoSize = false,
                    Width = 100,
                    Height = 30,
                    Location = new Point(10, 32),
                    AutoEllipsis = true,
                    Tag = clipIndex
                };
                strip.Controls.Add(lblName);

                var lblVol = new Label
                {
                    Text = $"VOL: {(int)(clip.Volume * 100)}%",
                    ForeColor = Color.Gainsboro,
                    AutoSize = true,
                    Location = new Point(10, 62),
                    Tag = clipIndex
                };
                strip.Controls.Add(lblVol);

                var trkVolume = new TrackBar
                {
                    Orientation = Orientation.Vertical,
                    Minimum = 0,
                    Maximum = 100,
                    Value = Math.Max(0, Math.Min(100, (int)(clip.Volume * 100))),
                    TickStyle = TickStyle.None,
                    Height = 100,
                    Width = 40,
                    Location = new Point(35, 82),
                    Tag = clipIndex
                };

                trkVolume.Scroll += (s, e) =>
                {
                    clip.Volume = trkVolume.Value / 100f;
                    lblVol.Text = $"VOL: {trkVolume.Value}%";

                    if (clip.ActiveReader != null)
                        clip.ActiveReader.Volume = clip.Volume;
                };

                strip.MouseDown += MixerTrack_MouseDown;
                lblTrack.MouseDown += MixerTrack_MouseDown;
                lblName.MouseDown += MixerTrack_MouseDown;
                lblVol.MouseDown += MixerTrack_MouseDown;
                trkVolume.MouseDown += MixerTrack_MouseDown;

                strip.Controls.Add(trkVolume);
                pnlMixerTracks.Controls.Add(strip);
            }

            pnlMixerTracks.ResumeLayout();
        }

        private void MixerTrack_MouseDown(object sender, MouseEventArgs e)
        {
            if (sender is Control c && c.Tag is int idx)
            {
                SelectClip(idx);
                pnlTrackSurface.Focus();
            }
        }

        // =========================
        //  RECORD
        // =========================
        private void ToggleRecord()
        {
            if (!isRecording)
            {
                StopPlaybackInternal(resetToZero: true);
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            string recDir = Path.Combine(Application.StartupPath, "Registrazioni");
            Directory.CreateDirectory(recDir);

            string fileName = "Rec_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".wav";
            lastRecord = Path.Combine(recDir, fileName);

            waveSource = new WaveInEvent();
            waveSource.WaveFormat = new WaveFormat(44100, 1);

            waveSource.DataAvailable += WaveSource_DataAvailable;
            waveSource.RecordingStopped += WaveSource_RecordingStopped;

            waveFile = new WaveFileWriter(lastRecord, waveSource.WaveFormat);

            isRecording = true;
            btnRec.BackColor = Color.FromArgb(120, 40, 40);

            showPlayhead = false;
            playheadSec = 0f;
            timelineOffsetX = 0;

            if (hScroll != null)
                hScroll.Value = hScroll.Minimum;

            waveSource.StartRecording();

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            isRecording = false;

            try
            {
                waveSource?.StopRecording();
            }
            catch
            {
            }

            btnRec.BackColor = Color.FromArgb(55, 60, 72);
        }

        private void WaveSource_DataAvailable(object sender, WaveInEventArgs e)
        {
            waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
            waveFile?.Flush();
        }

        private void WaveSource_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => WaveSource_RecordingStopped(sender, e)));
                return;
            }

            waveFile?.Dispose();
            waveFile = null;

            waveSource?.Dispose();
            waveSource = null;

            if (!string.IsNullOrEmpty(lastRecord) && File.Exists(lastRecord))
            {
                float durationSec;

                using (var r = new AudioFileReader(lastRecord))
                {
                    durationSec = (float)r.TotalTime.TotalSeconds;
                }

                if (durationSec < 0.05f)
                    durationSec = 0.05f;

                var newClip = new AudioClipInfo
                {
                    FilePath = lastRecord,
                    DisplayName = Path.GetFileNameWithoutExtension(lastRecord),
                    StartSec = 0f,
                    DurationSec = durationSec,
                    LaneIndex = clips.Count,
                    Volume = 1f,
                    Peaks = BuildWaveformPeaks(lastRecord, 2500)
                };

                clips.Add(newClip);
                selectedClipIndex = clips.Count - 1;

                UpdateProjectLength();

                playheadSec = 0f;
                showPlayhead = false;
                timelineOffsetX = 0;

                if (hScroll != null)
                    hScroll.Value = hScroll.Minimum;

                RebuildMixerTracks();
                LayoutAll();

                pnlRuler.Refresh();
                pnlTrackSurface.Refresh();
            }
        }

        // =========================
        //  COSTRUZIONE WAVEFORM
        // =========================
        private List<float> BuildWaveformPeaks(string filePath, int maxPeaks)
        {
            var peaks = new List<float>();

            using (var reader = new AudioFileReader(filePath))
            {
                int channels = reader.WaveFormat.Channels;
                int sampleRate = reader.WaveFormat.SampleRate;

                long totalFrames = (long)(reader.TotalTime.TotalSeconds * sampleRate);
                int samplesPerPeak = (int)Math.Max(1, totalFrames / Math.Max(1, maxPeaks));

                float[] buffer = new float[8192 * channels];
                int read;

                int frameCounter = 0;
                float currentPeak = 0f;

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i += channels)
                    {
                        float sampleAbs = 0f;

                        for (int ch = 0; ch < channels; ch++)
                        {
                            float s = Math.Abs(buffer[i + ch]);
                            if (s > sampleAbs)
                                sampleAbs = s;
                        }

                        if (sampleAbs > currentPeak)
                            currentPeak = sampleAbs;

                        frameCounter++;

                        if (frameCounter >= samplesPerPeak)
                        {
                            peaks.Add(Math.Min(1f, currentPeak));
                            currentPeak = 0f;
                            frameCounter = 0;
                        }
                    }
                }

                if (frameCounter > 0)
                    peaks.Add(Math.Min(1f, currentPeak));
            }

            if (peaks.Count == 0)
                peaks.Add(0f);

            return peaks;
        }

        // =========================
        //  PLAYBACK MULTI-TRACK
        // =========================
        private void StartPlayback()
        {
            if (clips.Count == 0)
            {
                MessageBox.Show("Nessuna traccia registrata.");
                return;
            }

            if (isRecording)
                StopRecording();

            StopPlaybackInternal(resetToZero: false);

            float projectEnd = GetProjectEndSec();

            if (playheadSec >= projectEnd)
            {
                playheadSec = 0f;
                timelineOffsetX = 0;
                if (hScroll != null)
                    hScroll.Value = hScroll.Minimum;
            }

            var mixerInputs = new List<ISampleProvider>();

            foreach (var clip in clips)
            {
                if (!File.Exists(clip.FilePath))
                    continue;

                var reader = new AudioFileReader(clip.FilePath);
                reader.Volume = clip.Volume;
                clip.ActiveReader = reader;

                float clipEnd = clip.StartSec + clip.DurationSec;

                if (playheadSec >= clipEnd)
                {
                    reader.Dispose();
                    clip.ActiveReader = null;
                    continue;
                }

                if (playheadSec < clip.StartSec)
                {
                    var delayedProvider = new OffsetSampleProvider(reader)
                    {
                        DelayBy = TimeSpan.FromSeconds(clip.StartSec - playheadSec)
                    };

                    mixerInputs.Add(delayedProvider);
                }
                else
                {
                    float localStart = playheadSec - clip.StartSec;
                    if (localStart < 0f) localStart = 0f;

                    reader.CurrentTime = TimeSpan.FromSeconds(localStart);
                    mixerInputs.Add(reader);
                }
            }

            if (mixerInputs.Count == 0)
            {
                DisposePlaybackResources(resetToZero: false);
                return;
            }

            var mixer = new MixingSampleProvider(mixerInputs)
            {
                ReadFully = false
            };

            outputDevice = new WaveOutEvent();
            outputDevice.Init(mixer);
            outputDevice.PlaybackStopped += OutputDevice_PlaybackStopped;

            playbackStartSec = playheadSec;
            playbackStartUtc = DateTime.UtcNow;

            outputDevice.Play();

            showPlayhead = true;
            timerPlay.Start();

            LayoutAll();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void OutputDevice_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OutputDevice_PlaybackStopped(sender, e)));
                return;
            }

            timerPlay.Stop();

            playheadSec = Math.Min(GetProjectEndSec(),
                playbackStartSec + (float)(DateTime.UtcNow - playbackStartUtc).TotalSeconds);

            DisposePlaybackResources(resetToZero: false);

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopAndReturnToStart()
        {
            timerPlay.Stop();
            StopPlaybackInternal(resetToZero: true);

            playheadSec = 0f;
            showPlayhead = false;

            timelineOffsetX = 0;
            if (hScroll != null)
                hScroll.Value = hScroll.Minimum;

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopPlaybackInternal(bool resetToZero)
        {
            if (outputDevice != null)
            {
                outputDevice.PlaybackStopped -= OutputDevice_PlaybackStopped;

                try
                {
                    if (outputDevice.PlaybackState != PlaybackState.Stopped)
                        outputDevice.Stop();
                }
                catch
                {
                }
            }

            DisposePlaybackResources(resetToZero);
        }

        private void DisposePlaybackResources(bool resetToZero)
        {
            outputDevice?.Dispose();
            outputDevice = null;

            foreach (var clip in clips)
            {
                clip.ActiveReader?.Dispose();
                clip.ActiveReader = null;
            }

            if (resetToZero)
                playheadSec = 0f;
        }

        private float GetProjectEndSec()
        {
            if (clips.Count == 0)
                return 0f;

            return clips.Max(c => c.StartSec + c.DurationSec);
        }

        private void UpdateProjectLength()
        {
            if (clips.Count == 0)
                timelineSeconds = 10f;
            else
                timelineSeconds = Math.Max(10f, clips.Max(c => c.StartSec + c.DurationSec));
        }

        // =========================
        //  TIMER
        // =========================
        private void timerPlay_Tick(object sender, EventArgs e)
        {
            if (outputDevice == null) return;
            if (outputDevice.PlaybackState != PlaybackState.Playing) return;

            playheadSec = playbackStartSec + (float)(DateTime.UtcNow - playbackStartUtc).TotalSeconds;

            float projectEnd = GetProjectEndSec();
            if (playheadSec > projectEnd)
                playheadSec = projectEnd;

            showPlayhead = true;

            AutoScrollToPlayhead();

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  SEEK + SELEZIONE + DRAG
        // =========================
        private void Timeline_MouseDownSeek(object sender, MouseEventArgs e)
        {
            pnlTrackSurface.Focus();

            int hitClipIndex = HitTestClip(e.Location);

            if (e.Button == MouseButtons.Left && hitClipIndex >= 0)
            {
                SelectClip(hitClipIndex);

                if (!isRecording && (outputDevice == null || outputDevice.PlaybackState != PlaybackState.Playing))
                {
                    isDraggingClip = true;
                    draggingClipIndex = hitClipIndex;
                    dragStartMouseX = e.X;
                    dragStartClipSec = clips[hitClipIndex].StartSec;
                    pnlTrackSurface.Capture = true;
                    pnlTrackSurface.Cursor = Cursors.SizeWE;
                }

                return;
            }

            int lane = GetLaneFromY(e.Y);
            if (lane >= 0 && lane < clips.Count)
                SelectClip(lane);
            else
                SelectClip(-1);

            int x = e.X - HeaderW;
            if (x < 0)
            {
                pnlTrackSurface.Invalidate();
                return;
            }

            int xTimeline = x + timelineOffsetX;
            float sec = xTimeline / (float)pixelsPerSecond;

            if (sec < 0) sec = 0;
            if (sec > timelineSeconds) sec = timelineSeconds;

            playheadSec = sec;
            showPlayhead = true;

            bool wasPlaying = outputDevice != null && outputDevice.PlaybackState == PlaybackState.Playing;

            if (wasPlaying)
            {
                StartPlayback();
                return;
            }

            AutoScrollToPlayhead();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void PnlTrackSurface_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingClip || draggingClipIndex < 0 || draggingClipIndex >= clips.Count)
                return;

            int deltaX = e.X - dragStartMouseX;
            float deltaSec = deltaX / (float)pixelsPerSecond;

            float newStart = dragStartClipSec + deltaSec;
            if (newStart < 0f)
                newStart = 0f;

            // snap leggero a 0.01 secondi
            newStart = (float)Math.Round(newStart, 2);

            clips[draggingClipIndex].StartSec = newStart;

            UpdateProjectLength();
            LayoutAll();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void PnlTrackSurface_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isDraggingClip)
                return;

            isDraggingClip = false;
            draggingClipIndex = -1;
            pnlTrackSurface.Capture = false;
            pnlTrackSurface.Cursor = Cursors.Default;

            UpdateProjectLength();
            LayoutAll();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private int HitTestClip(Point p)
        {
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                Rectangle rect = GetClipRectangle(clips[i]);
                if (rect.Contains(p))
                    return i;
            }

            return -1;
        }

        private Rectangle GetClipRectangle(AudioClipInfo clip)
        {
            int laneY = clip.LaneIndex * rowHeight + 8 - vScroll.Value;
            int laneH = rowHeight - 16;
            int xStart = HeaderW + (int)(clip.StartSec * pixelsPerSecond) - timelineOffsetX;
            int clipW = Math.Max(20, (int)(clip.DurationSec * pixelsPerSecond));

            return new Rectangle(xStart, laneY, clipW, laneH);
        }

        private int GetLaneFromY(int y)
        {
            return (y + vScroll.Value) / rowHeight;
        }

        private void SelectClip(int index)
        {
            selectedClipIndex = index;
            RebuildMixerTracks();
            pnlTrackSurface.Invalidate();
            pnlRuler.Invalidate();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedClip();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void DeleteSelectedClip()
        {
            if (selectedClipIndex < 0 || selectedClipIndex >= clips.Count)
                return;

            StopPlaybackInternal(resetToZero: false);
            timerPlay.Stop();

            var clipToRemove = clips[selectedClipIndex];

            clips.RemoveAt(selectedClipIndex);

            try
            {
                if (!string.IsNullOrEmpty(clipToRemove.FilePath) && File.Exists(clipToRemove.FilePath))
                    File.Delete(clipToRemove.FilePath);
            }
            catch
            {
            }

            for (int i = 0; i < clips.Count; i++)
                clips[i].LaneIndex = i;

            if (clips.Count == 0)
            {
                selectedClipIndex = -1;
                playheadSec = 0f;
                showPlayhead = false;
                timelineSeconds = 10f;
                timelineOffsetX = 0;

                if (hScroll != null)
                    hScroll.Value = hScroll.Minimum;
            }
            else
            {
                if (selectedClipIndex >= clips.Count)
                    selectedClipIndex = clips.Count - 1;

                UpdateProjectLength();

                float projectEnd = GetProjectEndSec();
                if (playheadSec > projectEnd)
                    playheadSec = projectEnd;
            }

            RebuildMixerTracks();
            LayoutAll();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  AUTO-SCROLL
        // =========================
        private void AutoScrollToPlayhead()
        {
            int viewW = Math.Max(1, pnlCenter.ClientSize.Width - vScroll.Width);
            int timelineViewW = Math.Max(1, viewW - HeaderW);

            int playPx = (int)(playheadSec * pixelsPerSecond);
            int xOnScreen = HeaderW + playPx - timelineOffsetX;

            int rightLimit = HeaderW + timelineViewW - 20;

            if (xOnScreen > rightLimit)
            {
                int newOffset = playPx - (timelineViewW - 20);
                newOffset = Math.Max(0, newOffset);

                if (newOffset > hScroll.Maximum)
                    newOffset = hScroll.Maximum;

                if (hScroll.Value != newOffset)
                {
                    hScroll.Value = newOffset;
                    timelineOffsetX = newOffset;
                }
            }
        }

        // =========================
        //  LAYOUT SCROLL
        // =========================
        private void LayoutAll()
        {
            int totalRows = Math.Max(12, clips.Count + 1);
            int contentHeight = rowHeight * totalRows;
            int viewHeight = pnlTrackSurface.ClientSize.Height;

            vScroll.Minimum = 0;
            vScroll.LargeChange = Math.Max(1, viewHeight);
            vScroll.Maximum = Math.Max(0, contentHeight - 1);
            vScroll.SmallChange = rowHeight;

            int totalWidth = (int)(timelineSeconds * pixelsPerSecond);

            int viewWidth = pnlCenter.ClientSize.Width - vScroll.Width;
            hScroll.Minimum = 0;
            hScroll.LargeChange = Math.Max(1, viewWidth);
            hScroll.Maximum = Math.Max(0, totalWidth - 1);

            if (hScroll.Value > hScroll.Maximum)
                hScroll.Value = hScroll.Maximum;

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  DRAW RULER
        // =========================
        private void PnlRuler_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(38, 41, 50));

            using var sepPen = new Pen(Color.FromArgb(70, 75, 90), 1);
            g.DrawLine(sepPen, HeaderW, pnlRuler.Height - 1, pnlRuler.Width, pnlRuler.Height - 1);

            using var f = new Font("Segoe UI", 9, FontStyle.Regular);
            using var br = new SolidBrush(Color.Gainsboro);

            int startX = HeaderW - timelineOffsetX;
            int seconds = (int)Math.Ceiling(timelineSeconds);

            for (int s = 0; s <= seconds; s++)
            {
                int x = startX + s * pixelsPerSecond;

                if (x < HeaderW) continue;
                if (x > pnlRuler.Width) break;

                g.DrawLine(Pens.Gray, x, 6, x, pnlRuler.Height - 6);

                if (s % 2 == 0)
                    g.DrawString(s.ToString(), f, br, x + 3, 10);
            }

            if (showPlayhead)
            {
                int xPlay = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX;
                using var playPen = new Pen(Color.WhiteSmoke, 2);
                g.DrawLine(playPen, xPlay, 0, xPlay, pnlRuler.Height);
            }
        }

        // =========================
        //  DRAW TRACKS + WAVEFORM
        // =========================
        private void PnlTrackSurface_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlTrackSurface.ClientSize.Width - vScroll.Width;
            int h = pnlTrackSurface.ClientSize.Height;
            int totalRows = Math.Max(12, clips.Count + 1);

            // Colonna sinistra tracce
            using (var headerBrush = new SolidBrush(Color.FromArgb(30, 33, 40)))
            {
                g.FillRectangle(headerBrush, 0, 0, HeaderW, h);
            }

            // Evidenzia riga selezionata
            if (selectedClipIndex >= 0 && selectedClipIndex < clips.Count)
            {
                int selY = selectedClipIndex * rowHeight - vScroll.Value;
                using var selBrush = new SolidBrush(Color.FromArgb(35, 70, 110, 150));
                g.FillRectangle(selBrush, 0, selY, w, rowHeight);
            }

            // Linee orizzontali
            using var penRow = new Pen(Color.FromArgb(40, 45, 55));
            for (int i = 0; i <= totalRows; i++)
            {
                int y = i * rowHeight - vScroll.Value;
                g.DrawLine(penRow, 0, y, w, y);
            }

            // Separatore header
            using var penHeaderSep = new Pen(Color.FromArgb(55, 60, 70));
            g.DrawLine(penHeaderSep, HeaderW, 0, HeaderW, h);

            // Linee verticali secondi
            using var penSec = new Pen(Color.FromArgb(35, 40, 48));
            int seconds = (int)Math.Ceiling(timelineSeconds);

            for (int s = 0; s <= seconds; s++)
            {
                int x = HeaderW + s * pixelsPerSecond - timelineOffsetX;
                if (x >= HeaderW && x <= w)
                    g.DrawLine(penSec, x, 0, x, h);
            }

            // Nomi tracce
            using var trackFont = new Font("Segoe UI", 9, FontStyle.Bold);

            for (int i = 0; i < totalRows; i++)
            {
                int y = i * rowHeight - vScroll.Value;
                if (y + rowHeight < 0 || y > h) continue;

                using var trackBrush = new SolidBrush(i == selectedClipIndex ? Color.White : Color.Gainsboro);
                g.DrawString($"TRACK {i + 1}", trackFont, trackBrush, 10, y + 10);
            }

            // Clip + waveform
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];

                Rectangle rect = GetClipRectangle(clip);

                if (rect.Bottom < 0 || rect.Top > h)
                    continue;

                DrawAudioClip(g, rect, clip, i == selectedClipIndex);
            }

            // Playhead
            if (showPlayhead)
            {
                int xPlay = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX;
                using var playPen = new Pen(Color.WhiteSmoke, 2);
                g.DrawLine(playPen, xPlay, 0, xPlay, h);
            }
        }

        private void DrawAudioClip(Graphics g, Rectangle rect, AudioClipInfo clip, bool isSelected)
        {
            if (rect.Right < HeaderW || rect.Left > pnlTrackSurface.Width)
                return;

            using var clipBrush = new SolidBrush(isSelected
                ? Color.FromArgb(70, 105, 185)
                : Color.FromArgb(48, 76, 140));

            using var clipBorder = new Pen(isSelected
                ? Color.FromArgb(255, 230, 140)
                : Color.FromArgb(110, 150, 240), isSelected ? 2 : 1);

            using var wavePen = new Pen(Color.FromArgb(220, 235, 255), 1);
            using var centerPen = new Pen(Color.FromArgb(90, 130, 210), 1);
            using var txtBrush = new SolidBrush(Color.WhiteSmoke);
            using var txtFont = new Font("Segoe UI", 8, FontStyle.Bold);

            g.FillRectangle(clipBrush, rect);
            g.DrawRectangle(clipBorder, rect);

            int centerY = rect.Top + rect.Height / 2;
            g.DrawLine(centerPen, rect.Left + 1, centerY, rect.Right - 1, centerY);

            if (clip.Peaks != null && clip.Peaks.Count > 0 && rect.Width > 2)
            {
                int peakCount = clip.Peaks.Count;
                int halfHeight = Math.Max(1, rect.Height / 2 - 4);

                for (int px = 0; px < rect.Width; px++)
                {
                    int startIndex = (int)(px * peakCount / (float)rect.Width);
                    int endIndex = (int)((px + 1) * peakCount / (float)rect.Width);

                    if (endIndex <= startIndex)
                        endIndex = startIndex + 1;

                    if (endIndex > peakCount)
                        endIndex = peakCount;

                    float max = 0f;
                    for (int i = startIndex; i < endIndex; i++)
                    {
                        if (clip.Peaks[i] > max)
                            max = clip.Peaks[i];
                    }

                    int amp = (int)(max * halfHeight);
                    int x = rect.Left + px;

                    g.DrawLine(wavePen, x, centerY - amp, x, centerY + amp);
                }
            }

            string text = $"{clip.DisplayName} ({clip.DurationSec:0.0}s)";
            g.DrawString(text, txtFont, txtBrush, rect.Left + 6, rect.Top + 4);
        }

        // =========================
        //  CLEANUP
        // =========================
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timerPlay?.Stop();

            try
            {
                waveSource?.StopRecording();
            }
            catch
            {
            }

            waveFile?.Dispose();
            waveSource?.Dispose();

            StopPlaybackInternal(resetToZero: false);

            base.OnFormClosing(e);
        }
    }
}