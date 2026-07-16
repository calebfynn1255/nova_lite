using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]

namespace NovaLite.Native;

/// <summary>
/// P/Invoke declarations matching llama.cpp C API version 2.24.0
/// (as shipped with LM Studio llama.cpp-win-x86_64-avx2-2.24.0).
/// </summary>
public static unsafe partial class LlamaCppBindings
{
    private const string LibName = "llama";

    // ── Sampler chain params ──────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaSamplerChainParams
    {
        [MarshalAs(UnmanagedType.I1)] public bool no_perf;
    }

    // ── Model params ──────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaModelParams
    {
        public int n_gpu_layers;
        public int split_mode;
        public int main_gpu;
        public IntPtr tensor_split;        // float*
        public IntPtr rpc_servers;         // const char**  (new in 2.24)
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;        // llama_model_kv_override*
        public IntPtr devices;             // ggml_backend_dev_t*  (new in 2.24)
        [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
        [MarshalAs(UnmanagedType.I1)] public bool use_mmap;
        [MarshalAs(UnmanagedType.I1)] public bool use_mlock;
        [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaContextParams
    {
        public uint n_ctx;
        public uint n_batch;
        public uint n_ubatch;
        public uint n_seq_max;
        public uint n_threads;
        public uint n_threads_batch;
        public int rope_scaling_type;
        public int pooling_type;
        public int attention_type;
        public float rope_freq_base;
        public float rope_freq_scale;
        public float yarn_ext_factor;
        public float yarn_attn_factor;
        public float yarn_beta_fast;
        public float yarn_beta_slow;
        public uint yarn_orig_ctx;
        public float defrag_thold;
        public IntPtr cb_eval;
        public IntPtr cb_eval_user_data;
        public int type_k;
        public int type_v;
        [MarshalAs(UnmanagedType.I1)] public bool swa_full;
        [MarshalAs(UnmanagedType.I1)] public bool logits_all;
        [MarshalAs(UnmanagedType.I1)] public bool embeddings;
        [MarshalAs(UnmanagedType.I1)] public bool offload_kqv;
        [MarshalAs(UnmanagedType.I1)] public bool flash_attn;
        [MarshalAs(UnmanagedType.I1)] public bool no_perf;
        public IntPtr abort_callback;
        public IntPtr abort_callback_data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaBatch
    {
        public int n_tokens;
        public int* token;
        public float* embd;
        public int* pos;
        public int* n_seq_id;
        public int** seq_id;
        public sbyte* logits;
    }

    // ── Backend ───────────────────────────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_backend_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_backend_init();

    [LibraryImport(LibName, EntryPoint = "llama_backend_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_backend_free();

    // ── Model lifecycle ───────────────────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_model_default_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaModelParams llama_model_default_params();

    /// <summary>Load model - new 2.24.0 API. Path must be UTF-8 null-terminated.</summary>
    [LibraryImport(LibName, EntryPoint = "llama_model_load_from_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_model_load_from_file(byte* path_model, LlamaModelParams @params);

    [LibraryImport(LibName, EntryPoint = "llama_model_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_model_free(IntPtr model);

    /// <summary>Get the vocab from a model (new 2.24 API - tokenize takes vocab* not model*)</summary>
    [LibraryImport(LibName, EntryPoint = "llama_model_get_vocab")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_model_get_vocab(IntPtr model);

    // ── Context lifecycle ─────────────────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_context_default_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaContextParams llama_context_default_params();

    /// <summary>New context - 2.24.0 uses llama_init_from_model instead of llama_new_context_with_model</summary>
    [LibraryImport(LibName, EntryPoint = "llama_init_from_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_init_from_model(IntPtr model, LlamaContextParams @params);

    [LibraryImport(LibName, EntryPoint = "llama_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_free(IntPtr ctx);

    [LibraryImport(LibName, EntryPoint = "llama_kv_cache_clear")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_kv_cache_clear(IntPtr ctx);

    // ── Tokenisation - 2.24 uses llama_vocab* not model* ─────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_tokenize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_tokenize(
        IntPtr vocab, byte* text, int text_len,
        int* tokens, int n_tokens_max,
        byte add_special,
        byte parse_special);

    [LibraryImport(LibName, EntryPoint = "llama_token_to_piece")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_token_to_piece(
        IntPtr vocab, int token, byte* buf, int length, int lstrip,
        byte special);

    // ── Decoding ──────────────────────────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_decode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_decode(IntPtr ctx, LlamaBatch batch);

    [LibraryImport(LibName, EntryPoint = "llama_batch_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaBatch llama_batch_init(int n_tokens, int embd, int n_seq_max);

    [LibraryImport(LibName, EntryPoint = "llama_batch_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_batch_free(LlamaBatch batch);

    [LibraryImport(LibName, EntryPoint = "llama_batch_get_one")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaBatch llama_batch_get_one(int* tokens, int n_tokens, int pos0, int seq_id);

    // ── New Sampler Chain API (2.24.0) ────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_sampler_chain_default_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaSamplerChainParams llama_sampler_chain_default_params();

    [LibraryImport(LibName, EntryPoint = "llama_sampler_chain_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_sampler_chain_init(LlamaSamplerChainParams @params);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_chain_add")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_sampler_chain_add(IntPtr chain, IntPtr smpl);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_sampler_free(IntPtr smpl);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_init_top_k")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_sampler_init_top_k(int k);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_init_top_p")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_sampler_init_top_p(float p, UIntPtr min_keep);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_init_temp")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_sampler_init_temp(float t);

    [LibraryImport(LibName, EntryPoint = "llama_sampler_init_dist")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_sampler_init_dist(uint seed);

    /// <summary>Sample the next token from the context at the last batch position.</summary>
    [LibraryImport(LibName, EntryPoint = "llama_sampler_sample")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_sampler_sample(IntPtr smpl, IntPtr ctx, int idx);

    // ── Vocab / special tokens ────────────────────────────────────────────────
    [LibraryImport(LibName, EntryPoint = "llama_vocab_n_tokens")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_vocab_n_tokens(IntPtr vocab);

    [LibraryImport(LibName, EntryPoint = "llama_vocab_eos")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int llama_vocab_eos(IntPtr vocab);

    [LibraryImport(LibName, EntryPoint = "llama_vocab_is_eog")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool llama_vocab_is_eog(IntPtr vocab, int token);
}
