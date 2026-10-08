using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PLL.Library;

internal static class DeveloperNoteLoader
{
    const string LoadingText = "Loading the Aeralis Foundation developer note...";
    const string DisabledText =
        "Developer note fetching is disabled. Enable PLL / Get Note and restart PEAK to fetch it.";

    const string Url =
        "https://raw.githubusercontent.com/Aeralis-Foundation/Notes/main/Games/Mods/PEAK/PLL/DeveloperNote.md";
    const int ByteLimit = 32 * 1024;
    const int TimeoutSeconds = 10;

    static UnityWebRequest? Request;
    static UnityWebRequestAsyncOperation? Operation;
    static DeveloperNoteRequestState State;

    internal static string? Result { get; private set; }
    internal static event Action<string>? ResultAvailable;

    internal static void Start(bool Enabled)
    {
        if (State != DeveloperNoteRequestState.Ready) return;
        if (!Enabled)
        {
            Finish(DisabledText);
            return;
        }

        State = DeveloperNoteRequestState.Loading;
        var Handler = new BoundedTextDownloadHandler(ByteLimit);
        UnityWebRequest? CreatedRequest = null;
        try
        {
            CreatedRequest = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbGET, Handler, null)
            {
                redirectLimit = 0,
                timeout = TimeoutSeconds
            };
            var StartedOperation = CreatedRequest.SendWebRequest();
            Request = CreatedRequest;
            Operation = StartedOperation;
            StartedOperation.completed += Complete;
        }
        catch (Exception Failure) when (Failure is UnityException or InvalidOperationException)
        {
            if (Operation is { } StartedOperation)
                StartedOperation.completed -= Complete;
            Operation = null;
            Request = null;
            if (CreatedRequest is null) Handler.Dispose();
            else CreatedRequest.Dispose();
            Fail(
                "Developer note unavailable because the request could not start.",
                "Developer note request could not start. Check the network connection.");
            return;
        }
        if (State == DeveloperNoteRequestState.Loading) Publish(LoadingText);
    }

    internal static void Shutdown()
    {
        var CurrentOperation = Operation;
        var CurrentRequest = Request;
        var WasLoading = State == DeveloperNoteRequestState.Loading;
        Operation = null;
        Request = null;
        ResultAvailable = null;
        if (WasLoading)
        {
            Result = "Developer note unavailable because PLL stopped before the request completed.";
            State = DeveloperNoteRequestState.Finished;
        }
        if (CurrentOperation is not null) CurrentOperation.completed -= Complete;
        if (CurrentRequest is null) return;
        try
        {
            if (!CurrentRequest.isDone) CurrentRequest.Abort();
        }
        finally
        {
            CurrentRequest.Dispose();
        }
    }

    static void Complete(AsyncOperation CompletedOperation)
    {
        if (State != DeveloperNoteRequestState.Loading
            || Operation is not { } CurrentOperation
            || !ReferenceEquals(CurrentOperation, CompletedOperation)) return;
        CurrentOperation.completed -= Complete;
        Operation = null;
        var CurrentRequest = Request
            ?? throw new InvalidOperationException("The active developer-note request is unavailable.");
        Request = null;
        try
        {
            var Handler = (BoundedTextDownloadHandler)CurrentRequest.downloadHandler;
            if (Handler.LimitExceeded)
            {
                Fail(
                    $"Developer note unavailable because the response exceeded {ByteLimit} bytes.",
                    $"Developer note response exceeded {ByteLimit} bytes. Reduce the published note size.");
                return;
            }
            if (CurrentRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                Fail(
                    "Developer note unavailable because the network could not be reached.",
                    "Developer note request could not reach the server. Check the network connection.");
                return;
            }
            if (CurrentRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Fail(
                    $"Developer note unavailable because the server returned HTTP {CurrentRequest.responseCode}.",
                    $"Developer note request returned HTTP {CurrentRequest.responseCode}. Check the published note URL.");
                return;
            }
            if (CurrentRequest.result != UnityWebRequest.Result.Success)
            {
                Fail(
                    "Developer note unavailable because the response could not be processed.",
                    "Developer note response could not be processed. Check the published note data.");
                return;
            }
            if (!Handler.TryGetText(out var Text))
            {
                Fail(
                    "Developer note unavailable because the response is not valid UTF-8 text.",
                    "Developer note response is not valid UTF-8 text. Publish a UTF-8 encoded note.");
                return;
            }
            if (string.IsNullOrWhiteSpace(Text))
            {
                Fail(
                    "Developer note unavailable because the response is empty.",
                    "Developer note response is empty. Publish note content.");
                return;
            }
            Finish(Text);
        }
        finally
        {
            CurrentRequest.Dispose();
        }
    }

    static void Fail(string DisplayText, string Warning)
    {
        Plugin.PLLLog.LogWarning(Warning);
        Finish(DisplayText);
    }

    static void Finish(string Text)
    {
        State = DeveloperNoteRequestState.Finished;
        Publish(Text);
    }

    static void Publish(string Text)
    {
        Result = Text;
        ResultAvailable?.Invoke(Text);
    }

    sealed class BoundedTextDownloadHandler : DownloadHandlerScript
    {
        const int ReceiveBufferSize = 4 * 1024;
        static readonly UTF8Encoding Utf8 = new(false, true);
        readonly byte[] Content;
        int Length;

        internal bool LimitExceeded { get; private set; }

        internal BoundedTextDownloadHandler(int ByteLimit) : base(new byte[ReceiveBufferSize]) =>
            Content = new byte[ByteLimit];

        protected override void ReceiveContentLengthHeader(ulong ContentLength) =>
            LimitExceeded = ContentLength > (ulong)Content.Length;

        protected override bool ReceiveData(byte[] Data, int DataLength)
        {
            if (LimitExceeded || DataLength > Content.Length - Length)
            {
                LimitExceeded = true;
                return false;
            }
            if (DataLength == 0) return true;
            Buffer.BlockCopy(Data, 0, Content, Length, DataLength);
            Length += DataLength;
            return true;
        }

        internal bool TryGetText(out string Text)
        {
            try
            {
                Text = Utf8.GetString(Content, 0, Length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                Text = string.Empty;
                return false;
            }
        }
    }

    enum DeveloperNoteRequestState
    {
        Ready,
        Loading,
        Finished
    }
}
