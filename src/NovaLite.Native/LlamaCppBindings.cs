using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]

namespace NovaLite.Native;

/// <summary>
/// P/Invoke declarations matching the bundled llama.cpp C API.
/// </summary>
public static unsafe partial class LlamaCppBindings
{
    private const string LibName = "llama";

    // ── Sampler chain params ──────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaSamplerChainParams
    {
        public byte no_perf;
    }

    // ── Model params ──────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaModelParams
    {
        public IntPtr devices;             // ggml_backend_dev_t* (NULL-terminated)
        public IntPtr tensor_buft_overrides;
        public int n_gpu_layers;
        public int split_mode;
        public int main_gpu;
        public IntPtr tensor_split;        // float*
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;        // llama_model_kv_override*
        public byte vocab_only;
        public byte use_mmap;
        public byte use_direct_io;
        public byte use_mlock;
        public byte check_tensors;
        public byte use_extra_bufts;
        public byte no_host;
        public byte no_alloc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaContextParams
    {
        public uint n_ctx;
        public uint n_batch;
        public uint n_ubatch;
        public uint n_seq_max;
        public uint n_rs_seq;
        public uint n_outputs_max;
        public int n_threads;
        public int n_threads_batch;
        public int ctx_type;
        public int rope_scaling_type;
        public int pooling_type;
        public int attention_type;
        public int flash_attn_type;
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
        public IntPtr abort_callback;
        public IntPtr abort_callback_data;
        public byte embeddings;
        public byte offload_kqv;
        public byte no_perf;
        public byte op_offload;
        public byte swa_full;
        public byte kv_unified;
        public IntPtr samplers;
        public UIntPtr n_samplers;
        public IntPtr ctx_other;
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

    [LibraryImport(LibName, EntryPoint = "llama_get_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr llama_get_memory(IntPtr ctx);

    [LibraryImport(LibName, EntryPoint = "llama_memory_clear")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_memory_clear(IntPtr memory, byte data);

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
    public static partial LlamaBatch llama_batch_get_one(int* tokens, int n_tokens);

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
