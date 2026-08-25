## 💡 Inspiration
Across Africa, Small and Medium Enterprises (SMEs), legal clinics, and accounting firms face a digital divide when it comes to adopting AI. While cloud-based LLMs like ChatGPT or Claude are powerful, they introduce three massive barriers for local businesses:
1. **Data Sovereignty & Privacy:** Uploading confidential NDAs, client financial records, or medical receipts to foreign cloud servers is a regulatory and privacy nightmare.
2. **Infrastructure Brittleness:** Frequent internet outages and unstable connectivity mean cloud-tools break during critical operating hours.
3. **Prohibitive Costs:** Recurring monthly SaaS fees and API token charges are hard to justify for bootstrapped startups and SMEs.

We realized that if we could bring the reasoning power of modern LLMs directly to the local hardware that African operators already use—specifically standard **8 GB RAM budget laptops**—we could democratize enterprise AI. This inspired the creation of **NovaLite**: a 100% offline, privacy-guaranteed knowledge workspace. 

## ⚙️ What it does
**NovaLite** is a desktop corporate AI workspace that turns an everyday laptop into a secure, offline intelligence hub. 

Designed specifically for the `corporate_enterprise` and `business_administration` domains, NovaLite allows operators to:
* **Parse Multi-Format Documents:** Users can drag and drop Word documents (`.docx`), Excel spreadsheets (`.xlsx`), PDFs, and even scanned images directly into the chat.
* **Intelligent OCR Fallback:** If a user uploads a scanned PDF receipt or an image-based contract, NovaLite automatically falls back to Windows native OCR to extract the text seamlessly.
* **Draft & Analyze:** Ask the AI to summarize quarterly revenue trends from a local spreadsheet, or draft a localized non-disclosure agreement based on local contexts.
* **Guarantee Absolute Privacy:** Because the inference engine runs entirely on-device, zero outbound network requests are made. Business IP never leaves the room.

## 🛠️ How we built it
NovaLite is engineered for maximum efficiency to fit within strict hardware envelopes.

To ensure the Llama-3.2-1B model would run flawlessly on an 8 GB system (even while users have Chrome and Excel open), we relied on GGUF `Q8_0` quantization. We modeled our RAM budget using the following memory footprint equation:

$$ \text{Total RAM (GB)} \approx \left( \frac{P \times Q}{8 \times 10^9} \right) + C_{ctx} + OS $$

Where:
* $$ P $$ = Parameters ($$ 1.23 \times 10^9 $$)
* $$ Q $$ = Average bits per weight ($$ 8 $$ for Q8_0)
* $$ C_{ctx} $$ = KV Cache memory for 4,096 tokens ($$ \approx 0.2 \text{ GB} $$)
* $$ OS $$ = Windows + UI Overhead ($$ \approx 3.0 \text{ GB} $$)

This theoretical math validated that our total system footprint would rest safely at **~4.4 GB** (leaving 3.6 GB completely free), allowing us to bypass the massive memory overhead of Python. We bound the inference directly to the native `llama.cpp` C++ shared libraries via P/Invoke (`NovaLite.Native`), while the frontend was crafted beautifully using C# Avalonia UI.

## 🚧 Challenges we ran into
* **The 8 GB RAM Ceiling:** The most brutal challenge was keeping the entire application footprint in memory. We had to build a custom `ContextWindowManager` that aggressively trims conversation history and intelligently chunks large documents to stay strictly within the 4,096-token KV cache budget calculated above.
* **Scanned PDFs and Ghost Text:** During testing, we realized many African businesses use scanned PDFs (images of documents) rather than digital-text PDFs. Initial parsers returned blank text. We had to engineer a fallback system that recursively extracts images from empty PDF pages and runs them through a local OCR engine to reconstruct the text layer.
* **UI Responsiveness:** Running heavy ML matrix multiplications on the CPU while keeping an Avalonia UI silky smooth required careful asynchronous thread management. We implemented a strict dispatcher architecture so token generation yields gracefully to the UI thread.

## 🏆 Accomplishments that we're proud of
* **Zero Cloud Dependency:** Successfully achieving 45–60 tokens/second on an integrated CPU/GPU laptop with absolutely no internet connection.
* **The OCR Pipeline:** Building a flawless, invisible transition between digital text extraction and OCR fallback. Users just drop a file, and it *works*, regardless of how the document was created.
* **Professional Aesthetics:** Transforming a raw inference engine into a beautifully polished, enterprise-ready product that non-technical business operators will actually want to use.

## 📚 What we learned
We learned that **quantization is an art as much as a science**. While 8B models can technically fit on 8GB machines, they push the system to the absolute edge. Scaling down to a highly intelligent 1B model (like Llama 3.2 1B) at a higher quantization (`Q8_0`) is the true "Goldilocks zone" for stable, lightning-fast edge AI. We also learned how deeply impactful offline technology can be; removing the anxiety of internet connectivity fundamentally changes how operators interact with software.

## 🚀 What's next for NovaLite
* **Local RAG (Retrieval-Augmented Generation):** We plan to integrate a local vector database (like ChromaDB or SQLite-VSS) so SMEs can point NovaLite at a folder of 10,000 past invoices and query their entire corporate history instantly.
* **Agentic File Automation:** Expanding our local `FileCommandService` so NovaLite can autonomously sort cluttered download folders, rename PDF invoices based on their content, and generate Excel reports on the local disk.
* **Cross-Platform Compilation:** Porting the Avalonia UI binaries to native Linux and macOS to support the growing number of developers and operators using affordable Linux distributions across the continent.
