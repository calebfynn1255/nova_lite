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
        
        // Offload slow blocking P/Invoke ops to a background thread so the UI
        // can render the user message and typing indicator immediately.
        await Task.Run(() =>
        {
            LlamaCppBindings.llama_memory_clear(LlamaCppBindings.llama_get_memory(_ctx), 1);
            _nPast = 0;
        }, ct);

        int[] tokens = await Task.Run(() => TokenizePrompt(prompt), ct);
        if (tokens.Length == 0)
            throw new Exception("Tokenization produced 0 tokens.");

        _logger.LogInformation("Tokenized to {Count} tokens, decoding prompt...", tokens.Length);
        await Task.Run(() => DecodePrompt(tokens), ct);

        _logger.LogInformation("Starting token generation loop...");
        byte[] pieceBuf = new byte[256];

        // Small buffer to detect multi-piece control tokens like <|im_end|>
        var pieceBuffer = new System.Text.StringBuilder(16);

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

            // Check if we hit any control markers (either complete or partial)
            int endIdx = buffered.IndexOf("<|im_end|>", StringComparison.Ordinal);
            if (endIdx == -1)
                endIdx = buffered.IndexOf("<|im_start|>", StringComparison.Ordinal);
            if (endIdx == -1)
                endIdx = buffered.IndexOf("<|", StringComparison.Ordinal); // stop if model tries to output any tag raw

            if (endIdx != -1)
            {
                // Yield the part before the tag, then stop
                var clean = buffered.Substring(0, endIdx);
                if (clean.Length > 0)
                    yield return clean;
                break;
            }

            // Hold back if the buffer ends with a partial tag prefix so we can detect it in the next loop
            bool isMaybeTag = buffered.EndsWith("<", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|i", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_e", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_en", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_end", StringComparison.Ordinal) ||
                              buffered.EndsWith("<|im_s", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_st", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_sta", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_star", StringComparison.Ordinal) || 
                              buffered.EndsWith("<|im_start", StringComparison.Ordinal);

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

        _logger.LogInformation("Generation complete.");
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

    private unsafe void DecodePrompt(int[] tokens)
    {
        const int maxBatchTokens = 512;
        for (int offset = 0; offset < tokens.Length; offset += maxBatchTokens)
        {
            int count = Math.Min(maxBatchTokens, tokens.Length - offset);
            fixed (int* pTok = &tokens[offset])
            {
                LlamaCppBindings.LlamaBatch batch = LlamaCppBindings.llama_batch_get_one(pTok, count);
                int ret = LlamaCppBindings.llama_decode(_ctx, batch);
                if (ret != 0)
                    throw new Exception($"llama_decode (prompt batch) returned {ret}");
            }

            _nPast += count;
        }
    }

    private unsafe void DecodeToken(int token)
    {
        int* pTok = stackalloc int[1];
        pTok[0] = token;
        LlamaCppBindings.LlamaBatch batch = LlamaCppBindings.llama_batch_get_one(pTok, 1);
        int ret = LlamaCppBindings.llama_decode(_ctx, batch);
        if (ret != 0)
            _logger.LogWarning("llama_decode (token) returned {Ret}", ret);
        _nPast += 1;
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
