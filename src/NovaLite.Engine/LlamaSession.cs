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
        
        // Wrap prompt in a robust user-assistant format so raw input doesn't confuse the model
        prompt = $"<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n";
        
        _logger.LogInformation("LlamaSession.InferAsync — prompt length: {Len}", prompt.Length);
        
        // Clear the prior conversation's KV state before starting a new prompt.
        LlamaCppBindings.llama_memory_clear(LlamaCppBindings.llama_get_memory(_ctx), 1);
        _nPast = 0;

        int[] tokens = TokenizePrompt(prompt);
        if (tokens.Length == 0)
            throw new Exception("Tokenization produced 0 tokens.");

        _logger.LogInformation("Tokenized to {Count} tokens, decoding prompt batch...", tokens.Length);
        DecodePrompt(tokens);

        _logger.LogInformation("Starting token generation loop...");
        byte[] pieceBuf = new byte[256];

        for (int i = 0; i < maxTokens; i++)
        {
            ct.ThrowIfCancellationRequested();

            int newToken = SampleNext();

            if (IsEog(newToken))
            {
                _logger.LogInformation("EOG token reached at step {I}", i);
                break;
            }

            string piece = TokenToPiece(newToken, pieceBuf);
            if (!string.IsNullOrEmpty(piece))
                yield return piece;

            DecodeToken(newToken);
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
        fixed (int* pTok = tokens)
        {
            LlamaCppBindings.LlamaBatch batch = LlamaCppBindings.llama_batch_get_one(pTok, tokens.Length);
            int ret = LlamaCppBindings.llama_decode(_ctx, batch);
            if (ret != 0)
                throw new Exception($"llama_decode (prompt) returned {ret}");
            _nPast += tokens.Length;
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
