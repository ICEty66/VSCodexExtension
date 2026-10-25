// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Threading.Tasks;

namespace VSCodex.Services;

/// <summary>Provides the voice Input Service implementation.</summary>
public sealed class VoiceInputService : IVoiceInputService
{
    /// <summary>Named number used by this type.</summary>
    private const float Numeric0Point35F = 0.35F;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric1Point5 = 1.5;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric10 = 10;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric6 = 6;

    /// <summary>Stores the gate.</summary>
    private readonly object _gate = new();

    /// <summary>Stores the transcript.</summary>
    private readonly Subject<string> _transcript = new();

    /// <summary>Stores the status.</summary>
    private readonly BehaviorSubject<string> _status = new("语音输入已就绪");

    /// <summary>Stores the recognizer.</summary>
    private SpeechRecognitionEngine? _recognizer;

    /// <summary>Stores the start Task.</summary>
    private Task? _startTask;

    /// <summary>Stores the is Available.</summary>
    private bool _isAvailable = true;

    /// <summary>Stores the is Listening.</summary>
    private bool _isListening;

    /// <summary>Stores the start Requested.</summary>
    private bool _startRequested;

    /// <summary>Stores the disposed.</summary>
    private bool _disposed;

    /// <summary>Gets the transcript.</summary>
    public IObservable<string> Transcript => _transcript.AsObservable();

    /// <summary>Gets the status.</summary>
    public IObservable<string> Status => _status.AsObservable();

    /// <summary>Gets the is Available.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _isAvailable;
            }
        }
    }

    /// <summary>Gets the is Listening.</summary>
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _isListening;
            }
        }
    }

    /// <summary>Starts the operation.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                SafeStatus("语音输入已释放");
                return;
            }

            if (!_isAvailable)
            {
                SafeStatus("此 Windows 安装上不支持语音输入");
                return;
            }

            if (_isListening)
            {
                SafeStatus("Listening");
                return;
            }

            _startRequested = true;
            if (_startTask?.IsCompleted == false)
            {
                SafeStatus("语音输入正在启动");
                return;
            }

            SafeStatus("正在启动语音输入");
            _startTask = Task.Run(StartListeningCore);
        }
    }

    /// <summary>Stops the operation.</summary>
    public void Stop()
    {
        SpeechRecognitionEngine? recognizer;
        var wasListening = false;
        lock (_gate)
        {
            _startRequested = false;
            recognizer = _recognizer;
            wasListening = _isListening;
            _isListening = false;
        }

        if (recognizer is not null && wasListening)
        {
            try
            {
                recognizer.RecognizeAsyncCancel();
            }
            catch (Exception ex) when (IsSpeechInfrastructureException(ex))
            {
                ReleaseRecognizer(recognizer);
                SafeStatus($"识别器出错后语音输入已停止：{ex.Message}");
                return;
            }
        }

        SafeStatus("语音输入已停止");
    }

    /// <summary>Performs the dispose operation.</summary>
    public void Dispose()
    {
        SpeechRecognitionEngine? recognizer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _startRequested = false;
            _isListening = false;
            recognizer = _recognizer;
            _recognizer = null;
        }

        ReleaseRecognizer(recognizer);
        _transcript.Dispose();
        _status.Dispose();
    }

    /// <summary>Starts listening Core.</summary>
    private void StartListeningCore()
    {
        SpeechRecognitionEngine? recognizer = null;
        try
        {
            recognizer = GetOrCreateRecognizer();
            lock (_gate)
            {
                if (_disposed || !_startRequested)
                {
                    if (!ReferenceEquals(_recognizer, recognizer))
                    {
                        ReleaseRecognizer(recognizer);
                    }

                    return;
                }
            }

            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            lock (_gate)
            {
                _isListening = true;
                _isAvailable = true;
            }

            SafeStatus("Listening");
        }
        catch (Exception ex) when (IsSpeechInfrastructureException(ex))
        {
            lock (_gate)
            {
                _isListening = false;
                _startRequested = false;
                _isAvailable = false;
                if (ReferenceEquals(_recognizer, recognizer))
                {
                    _recognizer = null;
                }
            }

            ReleaseRecognizer(recognizer);
            SafeStatus($"语音输入不可用：{ex.Message}");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _isListening = false;
                _startRequested = false;
            }

            SafeStatus($"语音输入启动失败：{ex.Message}");
        }
    }

    /// <summary>Gets or Create Recognizer.</summary>
    /// <returns>The get Or Create Recognizer result.</returns>
    private SpeechRecognitionEngine GetOrCreateRecognizer()
    {
        lock (_gate)
        {
            if (_recognizer is not null)
            {
                return _recognizer;
            }
        }

        var created = CreateRecognizer();
        lock (_gate)
        {
            if (_disposed || !_startRequested)
            {
                return created;
            }

            _recognizer = created;
            return created;
        }
    }

    /// <summary>Creates recognizer.</summary>
    /// <returns>The create Recognizer result.</returns>
    private SpeechRecognitionEngine CreateRecognizer()
    {
        var recognizers = SpeechRecognitionEngine.InstalledRecognizers().ToList();
        var recognizerInfo = recognizers
            .FirstOrDefault(x => Equals(x.Culture, CultureInfo.CurrentUICulture))
            ?? recognizers
                .FirstOrDefault(x => string.Equals(x.Culture.TwoLetterISOLanguageName, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            ?? recognizers.FirstOrDefault() ?? throw new InvalidOperationException("未安装 Windows 语音识别器。");

        var recognizer = new SpeechRecognitionEngine(recognizerInfo);
        recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(Numeric10);
        recognizer.BabbleTimeout = TimeSpan.FromSeconds(Numeric6);
        recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(1);
        recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(Numeric1Point5);
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SetInputToDefaultAudioDevice();
        recognizer.SpeechDetected += OnSpeechDetected;
        recognizer.SpeechRecognized += OnSpeechRecognized;
        recognizer.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
        recognizer.RecognizeCompleted += OnRecognizeCompleted;
        return recognizer;
    }

    /// <summary>Handles the speech Detected event.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnSpeechDetected(object sender, EventArgs e)
    {
        if (!IsListening)
        {
            return;
        }

        SafeStatus("语音输入检测到语音");
    }

    /// <summary>Handles the recognize Completed event.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnRecognizeCompleted(object sender, RecognizeCompletedEventArgs e)
    {
        lock (_gate)
        {
            _isListening = false;
            if (e.Error is not null)
            {
                _startRequested = false;
            }
        }

        SafeStatus(e.Error is null ? "语音输入已停止" : $"语音输入已停止：{e.Error.Message}");
    }

    /// <summary>Handles the speech Recognized event.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
    {
        var result = e.Result;
        if (result is null || string.IsNullOrWhiteSpace(result.Text))
        {
            if (IsListening)
            {
                SafeStatus("语音输入听到语音，但没有可用的转写文本");
            }

            return;
        }

        var text = result.Text.Trim();
        SafeTranscript(text);
        SafeStatus(result.Confidence < Numeric0Point35F
            ? $"语音输入捕获到低置信度转写，请核对：{text}"
            : $"语音输入已捕获：{text}");
    }

    /// <summary>Handles the speech Recognition Rejected event.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
    {
        if (!IsListening)
        {
            return;
        }

        SafeStatus("语音输入听到语音但无法识别");
    }

    /// <summary>Performs the release Recognizer operation.</summary>
    /// <param name="recognizer">The recognizer.</param>
    private void ReleaseRecognizer(SpeechRecognitionEngine? recognizer)
    {
        if (recognizer is null)
        {
            return;
        }

        try
        {
            recognizer.SpeechDetected -= OnSpeechDetected;
            recognizer.SpeechRecognized -= OnSpeechRecognized;
            recognizer.SpeechRecognitionRejected -= OnSpeechRecognitionRejected;
            recognizer.RecognizeCompleted -= OnRecognizeCompleted;
            recognizer.Dispose();
        }
        catch (Exception ex) when (IsSpeechInfrastructureException(ex))
        {
        }
    }

    /// <summary>Performs the safe Status operation.</summary>
    /// <param name="status">The status.</param>
    private void SafeStatus(string status)
    {
        try
        {
            _status.OnNext(status);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Performs the safe Transcript operation.</summary>
    /// <param name="text">The text.</param>
    private void SafeTranscript(string text)
    {
        try
        {
            _transcript.OnNext(text);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Determines whether is Speech Infrastructure Exception.</summary>
    /// <param name="ex">The ex.</param>
    /// <returns><see langword="true"/> when is Speech Infrastructure Exception succeeds; otherwise, <see langword="false"/>.</returns>
    private bool IsSpeechInfrastructureException(Exception ex)
        => ex is InvalidOperationException
        || ex is COMException
        || ex is InvalidComObjectException
        || ex is ObjectDisposedException;
}
