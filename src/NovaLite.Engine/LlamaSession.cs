using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using NovaLite.Native;

namespace NovaLite.Engine;

public sealed class LlamaSession : IInferenceSession
{
    private readonly IntPtr _model;
    private readonly IntPtr _vocab;
    private readonly IntPtr _ctx;
    private readonly IntPtr _sampler;
    private readonly ILogger _logger;
    private bool _disposed;
    private int _nPast;
    private readonly List<int> _cachedTokens = new List<int>();

    public LlamaSession(IntPtr model, IntPtr vocab, IntPtr ctx, IntPtr sampler, ILogger logger)
    {
        _model = model;
        _vocab = vocab;
        _ctx = ctx;
        _sampler = sampler;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> InferAsync(
        string prompt,
        InferenceOptions? options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int maxTokens = options?.MaxTokens > 0 ? options.MaxTokens : 512;
        
        _logger.LogInformation("LlamaSession.InferAsync — prompt length: {Len}", prompt.Length);
        
        int[] tokens = await Task.Run(() => TokenizePrompt(prompt), ct);
        if (tokens.Length == 0)
            throw new Exception("Tokenization produced 0 tokens.");

        const int maxPromptTokens = 16000;
        if (tokens.Length > maxPromptTokens)
        {
            _logger.LogWarning("Prompt exceeds safe decode budget ({TokenCount} tokens); truncating to {Limit} tokens", tokens.Length, maxPromptTokens);
            tokens = tokens.Skip(tokens.Length - maxPromptTokens).ToArray();
        }

        int matchLen = 0;
        while (matchLen < tokens.Length && matchLen < _cachedTokens.Count && tokens[matchLen] == _cachedTokens[matchLen])
        {
            matchLen++;
        }

        if (matchLen < _cachedTokens.Count)
        {
            await Task.Run(() =>
            {
                LlamaCppBindings.llama_memory_clear(LlamaCppBindings.llama_get_memory(_ctx), 1);
            }, ct);
            _nPast = 0;
            _cachedTokens.Clear();
            matchLen = 0;
        }
        else
        {
            _nPast = matchLen;
        }

        int[] newTokens = tokens.Skip(matchLen).ToArray();
        
        _logger.LogInformation("Tokenized to {Count} tokens, re-using {Match} tokens, decoding {New} new tokens...", tokens.Length, matchLen, newTokens.Length);

        if (newTokens.Length > 0)
        {
            const int maxBatchTokens = 512;
            int totalChunks = (int)Math.Ceiling((double)newTokens.Length / maxBatchTokens);
            
            for (int i = 0; i < totalChunks; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                int offset = i * maxBatchTokens;
                int count = Math.Min(maxBatchTokens, newTokens.Length - offset);
                int[] chunk = newTokens.Skip(offset).Take(count).ToArray();
                
                await Task.Run(() =>
                {
                    unsafe
                    {
                        fixed (int* pTok = chunk)
                        {
                            LlamaCppBindings.LlamaBatch batch = LlamaCppBindings.llama_batch_get_one(pTok, count, _nPast, 0);
                            int ret = LlamaCppBindings.llama_decode(_ctx, batch);
                            if (ret != 0) throw new Exception($"llama_decode returned {ret}");
                        }
                    }
                }, ct);
                
                _nPast += count;
            }
            
            _cachedTokens.AddRange(newTokens);
        }

        _logger.LogInformation("Starting token generation loop...");
        byte[] pieceBuf = new byte[256];

        // Small buffer to detect multi-piece control tokens. Some GGUF models emit
        // their chat-template delimiter as normal text unless we stop it here.
        var pieceBuffer = new System.Text.StringBuilder(16);
        var stopSequences = new[]
            {
                "<|im_end|>",
                "<|im_start|>",
                "<|eot_id|>",
                "<|end_of_text|>",
                "<\uFF5Cend of sentence\uFF5C>"
            }
            .Concat(options?.StopSequences ?? [])
            .Where(sequence => !string.IsNullOrWhiteSpace(sequence))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        for (int i = 0; i < maxTokens; i++)
        {
            ct.ThrowIfCancellationRequested();

            // SampleNext and DecodeToken are fast (<1ms each) — no Task.Run needed
            int newToken = SampleNext();

            if (IsEog(newToken))
            {
                _logger.LogInformation("EOG at step {I}", i);
                break;
            }

            string piece = TokenToPiece(newToken, pieceBuf);
            pieceBuffer.Append(piece);

            string buffered = pieceBuffer.ToString();

            // Check if we hit any configured end-of-turn control marker.
            int endIdx = FindFirstStopSequence(buffered, stopSequences);

            if (endIdx != -1)
            {
                // Yield the part before the tag, then stop
                var clean = buffered.Substring(0, endIdx);
                if (clean.Length > 0)
                    yield return clean;
                break;
            }

            // Hold back a partial delimiter so it cannot leak into the visible stream.
            bool isMaybeTag = EndsWithStopPrefix(buffered, stopSequences);

            if (!isMaybeTag)
            {
                if (buffered.Length > 0)
                    yield return buffered;
                pieceBuffer.Clear();
            }

            DecodeToken(newToken);

            // Yield control so Avalonia can render each token as it arrives
            await Task.Yield();
        }

        // Flush any content held back in the tag-detection buffer
        var remaining = pieceBuffer.ToString();
        if (remaining.Length > 0)
        {
            // Strip any partial tag prefix before yielding
            int tagStart = FindFirstStopSequencePrefix(remaining, stopSequences);
            var flushed = tagStart >= 0 ? remaining[..tagStart] : remaining;
            if (flushed.Length > 0)
                yield return flushed;
        }

        _logger.LogInformation("Generation complete.");
    }

    private static int FindFirstStopSequence(string text, IEnumerable<string> stopSequences)
    {
        var firstIndex = -1;
        foreach (var stopSequence in stopSequences)
        {
            var index = text.IndexOf(stopSequence, StringComparison.Ordinal);
            if (index >= 0 && (firstIndex < 0 || index < firstIndex))
                firstIndex = index;
        }

        return firstIndex;
    }

    private static bool EndsWithStopPrefix(string text, IEnumerable<string> stopSequences)
    {
        foreach (var stopSequence in stopSequences)
        {
            for (var prefixLength = 1; prefixLength < stopSequence.Length; prefixLength++)
            {
                if (text.EndsWith(stopSequence[..prefixLength], StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static int FindFirstStopSequencePrefix(string text, IEnumerable<string> stopSequences)
    {
        var firstIndex = -1;
        foreach (var stopSequence in stopSequences)
        {
            for (var prefixLength = 1; prefixLength <= stopSequence.Length; prefixLength++)
            {
                var index = text.IndexOf(stopSequence[..prefixLength], StringComparison.Ordinal);
                if (index >= 0 && (firstIndex < 0 || index < firstIndex))
                    firstIndex = index;
            }
        }

        return firstIndex;
    }

    // ── Unsafe helpers ────────────────────────────────────────────────────────

    private unsafe int[] TokenizePrompt(string prompt)
    {
        int byteCount = Encoding.UTF8.GetByteCount(prompt);
        byte[] utf8 = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(prompt, 0, prompt.Length, utf8, 0);

        int[] tokens = new int[byteCount + 64];
        int count;

        fixed (byte* pText = utf8)
        fixed (int* pTok = tokens)
        {
            count = LlamaCppBindings.llama_tokenize(_vocab, pText, byteCount, pTok, tokens.Length, 1, 1);

            if (count < 0)
            {
                tokens = new int[-count + 4];
                fixed (int* pTok2 = tokens)
                    count = LlamaCppBindings.llama_tokenize(_vocab, pText, byteCount, pTok2, tokens.Length, 1, 1);
            }
        }

        if (count <= 0)
            throw new Exception($"llama_tokenize failed with code: {count}");

        var result = new int[count];
        Array.Copy(tokens, result, count);
        return result;
    }



    private unsafe void DecodeToken(int token)
    {
        int* pTok = stackalloc int[1];
        pTok[0] = token;
        LlamaCppBindings.LlamaBatch batch = LlamaCppBindings.llama_batch_get_one(pTok, 1, _nPast, 0);
        int ret = LlamaCppBindings.llama_decode(_ctx, batch);
        if (ret != 0)
            _logger.LogWarning("llama_decode (token) returned {Ret}", ret);
        _nPast += 1;
        _cachedTokens.Add(token);
    }

    private int SampleNext()
        => LlamaCppBindings.llama_sampler_sample(_sampler, _ctx, -1);

    private bool IsEog(int token)
        => LlamaCppBindings.llama_vocab_is_eog(_vocab, token);

    private unsafe string TokenToPiece(int token, byte[] buf)
    {
        fixed (byte* pBuf = buf)
        {
            int n = LlamaCppBindings.llama_token_to_piece(_vocab, token, pBuf, buf.Length, 0, 0);
            return n > 0 ? Encoding.UTF8.GetString(buf, 0, n) : string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("LlamaSession disposing...");
        LlamaCppBindings.llama_sampler_free(_sampler);
        LlamaCppBindings.llama_free(_ctx);
    }
}
