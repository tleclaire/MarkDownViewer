using Markdig;
using System.IO;
using System.Text;

namespace MdViewer.Shared;

/// <summary>
/// Konvertiert Markdown in HTML mit vollständiger GFM-Unterstützung
/// und GitHub-inspiriertem CSS (hell/dunkel via prefers-color-scheme).
/// </summary>
public static class MarkdownRenderService
{
    private static readonly MarkdownPipeline Pipeline;

    static MarkdownRenderService()
    {
        Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // Alle erweiterten Extensions aktivieren (inkl. AutoIdentifiers)
            .UseEmojiAndSmiley()       // :smile: -> 😄
            .UseSoftlineBreakAsHardlineBreak() // Zeilenumbruch = <br>
            .Build();
    }

    /// <summary>
    /// Rendert einen Markdown-String als vollständiges HTML-Dokument.
    /// </summary>
    public static string Render(string markdown, string? baseUrl = null)
    {
        if (string.IsNullOrEmpty(markdown))
            return GetEmptyDocument();

        var bodyHtml = Markdown.ToHtml(markdown, Pipeline);
        return WrapInHtmlDocument(bodyHtml, baseUrl);
    }

    /// <summary>
    /// Rendert eine Markdown-Datei als vollständiges HTML-Dokument.
    /// </summary>
    public static async Task<string> RenderFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return GetErrorDocument($"Datei nicht gefunden: {filePath}");

        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var markdown = await reader.ReadToEndAsync();
            var baseUrl = PathToFileUrl(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            return Render(markdown, baseUrl);
        }
        catch (Exception ex)
        {
            return GetErrorDocument($"Fehler beim Lesen der Datei: {ex.Message}");
        }
    }

    private static string WrapInHtmlDocument(string bodyHtml, string? baseUrl = null)
    {
        var baseTag = baseUrl != null
            ? $"\n<base href=\"{System.Net.WebUtility.HtmlEncode(baseUrl)}\">"
            : "";

        return $@"<!DOCTYPE html>
<html lang=""de"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>Markdown Viewer</title>{baseTag}
<style>
{CssStyles}
</style>
</head>
<body>
<article class=""markdown-body"">
{bodyHtml}
</article>
</body>
</html>";
    }

    private static string PathToFileUrl(string directoryPath)
    {
        // Windows-Pfad in file:/// URL konvertieren
        // "D:\path\to\dir" → "file:///D:/path/to/dir/"
        var normalized = directoryPath.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        return $"file://{normalized}/";
    }

    private static string GetEmptyDocument()
    {
        return WrapInHtmlDocument("<p><em>(Leere Datei)</em></p>");
    }

    private static string GetErrorDocument(string message)
    {
        return $@"<!DOCTYPE html>
<html lang=""de"">
<head><meta charset=""utf-8""><title>Fehler</title>
<style>
body {{ font-family: 'Segoe UI', sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #f0f0f0; color: #333; }}
.error {{ text-align: center; padding: 2rem; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }}
h1 {{ color: #d32f2f; font-size: 1.5rem; }}
p {{ color: #666; }}
</style>
</head>
<body>
<div class=""error"">
<h1>⚠️ Fehler</h1>
<p>{System.Net.WebUtility.HtmlEncode(message)}</p>
</div>
</body>
</html>";
    }

    // ──────────────────────────────────────────────
    //  CSS — GitHub-inspirierte Light/Dark Themes
    // ──────────────────────────────────────────────
    private const string CssStyles = @"
*, *::before, *::after { box-sizing: border-box; }

:root {
  --color-prettylights-syntax-comment: #6e7781;
  --color-prettylights-syntax-constant: #0550ae;
  --color-prettylights-syntax-entity: #8250df;
  --color-prettylights-syntax-storage-modifier-import: #24292f;
  --color-prettylights-syntax-entity-tag: #116329;
  --color-prettylights-syntax-keyword: #cf222e;
  --color-prettylights-syntax-string: #0a3069;
  --color-prettylights-syntax-variable: #953800;
  --color-prettylights-syntax-brackethighlighter-unmatched: #82071e;
  --color-prettylights-syntax-invalid-illegal-text: #f6f8fa;
  --color-prettylights-syntax-invalid-illegal-bg: #82071e;
  --color-prettylights-syntax-carriage-return-text: #f6f8fa;
  --color-prettylights-syntax-carriage-return-bg: #cf222e;
  --color-prettylights-syntax-string-regexp: #116329;
  --color-prettylights-syntax-markup-list: #3b2300;
  --color-prettylights-syntax-markup-heading: #0550ae;
  --color-prettylights-syntax-markup-italic: #24292f;
  --color-prettylights-syntax-markup-bold: #24292f;
  --color-prettylights-syntax-markup-deleted-text: #82071e;
  --color-prettylights-syntax-markup-deleted-bg: #ffebe9;
  --color-prettylights-syntax-markup-inserted-text: #116329;
  --color-prettylights-syntax-markup-inserted-bg: #dafbe1;
  --color-prettylights-syntax-markup-changed-text: #953800;
  --color-prettylights-syntax-markup-changed-bg: #ffd8b5;
  --color-prettylights-syntax-markup-ignored-text: #eaeef2;
  --color-prettylights-syntax-markup-ignored-bg: #0550ae;
  --color-prettylights-syntax-meta-diff-range: #8250df;
  --color-prettylights-syntax-brackethighlighter-angle: #57606a;
  --color-prettylights-syntax-sublimelinter-gutter-mark: #8c959f;
  --color-prettylights-syntax-constant-other-reference-link: #0a3069;
  --color-btn-text: #24292f;
  --color-btn-bg: #f6f8fa;
  --color-btn-shadow: 0 1px 0 rgba(31,35,40,0.04);
  --color-btn-inset-shadow: inset 0 1px 0 rgba(255,255,255,0.25);
  --color-btn-hover-bg: #f3f4f6;
  --color-btn-hover-border: #d0d7de;
  --color-btn-active-bg: #ebecf0;
  --color-btn-active-border: #d0d7de;
  --color-btn-primary-text: #fff;
  --color-btn-primary-bg: #1f883d;
  --color-btn-primary-border: rgba(31,35,40,0.15);
  --color-btn-primary-hover-bg: #1a7f37;
  --color-btn-primary-active-bg: #157b35;
  --color-fg-default: #1f2328;
  --color-fg-muted: #656d76;
  --color-fg-subtle: #6e7781;
  --color-canvas-default: #ffffff;
  --color-canvas-subtle: #f6f8fa;
  --color-border-default: #d0d7de;
  --color-border-muted: #d8dee4;
  --color-neutral-muted: rgba(175,184,193,0.2);
  --color-accent-fg: #0969da;
  --color-accent-emphasis: #0969da;
  --color-attention-subtle: #fff8c5;
  --color-danger-fg: #d1242f;
}

@media (prefers-color-scheme: dark) {
  :root {
    --color-prettylights-syntax-comment: #8b949e;
    --color-prettylights-syntax-constant: #79c0ff;
    --color-prettylights-syntax-entity: #d2a8ff;
    --color-prettylights-syntax-storage-modifier-import: #c9d1d9;
    --color-prettylights-syntax-entity-tag: #7ee787;
    --color-prettylights-syntax-keyword: #ff7b72;
    --color-prettylights-syntax-string: #a5d6ff;
    --color-prettylights-syntax-variable: #ffa657;
    --color-prettylights-syntax-brackethighlighter-unmatched: #f85149;
    --color-prettylights-syntax-invalid-illegal-text: #f0f6fc;
    --color-prettylights-syntax-invalid-illegal-bg: #8e1519;
    --color-prettylights-syntax-carriage-return-text: #f0f6fc;
    --color-prettylights-syntax-carriage-return-bg: #b62324;
    --color-prettylights-syntax-string-regexp: #7ee787;
    --color-prettylights-syntax-markup-list: #f2cc60;
    --color-prettylights-syntax-markup-heading: #1f6feb;
    --color-prettylights-syntax-markup-italic: #c9d1d9;
    --color-prettylights-syntax-markup-bold: #c9d1d9;
    --color-prettylights-syntax-markup-deleted-text: #ffdcd7;
    --color-prettylights-syntax-markup-deleted-bg: #67060c;
    --color-prettylights-syntax-markup-inserted-text: #aff5b4;
    --color-prettylights-syntax-markup-inserted-bg: #0a5117;
    --color-prettylights-syntax-markup-changed-text: #ffd8b5;
    --color-prettylights-syntax-markup-changed-bg: #6c3b02;
    --color-prettylights-syntax-markup-ignored-text: #c9d1d9;
    --color-prettylights-syntax-markup-ignored-bg: #0d4197;
    --color-prettylights-syntax-meta-diff-range: #d2a8ff;
    --color-prettylights-syntax-brackethighlighter-angle: #8b949e;
    --color-prettylights-syntax-sublimelinter-gutter-mark: #484f58;
    --color-prettylights-syntax-constant-other-reference-link: #a5d6ff;
    --color-btn-text: #c9d1d9;
    --color-btn-bg: #21262d;
    --color-btn-shadow: 0 0 transparent;
    --color-btn-inset-shadow: 0 0 transparent;
    --color-btn-hover-bg: #30363d;
    --color-btn-hover-border: #8b949e;
    --color-btn-active-bg: #363b44;
    --color-btn-active-border: #8b949e;
    --color-btn-primary-text: #fff;
    --color-btn-primary-bg: #238636;
    --color-btn-primary-border: rgba(240,246,252,0.1);
    --color-btn-primary-hover-bg: #2ea043;
    --color-btn-primary-active-bg: #2b9939;
    --color-fg-default: #e6edf3;
    --color-fg-muted: #8b949e;
    --color-fg-subtle: #6e7681;
    --color-canvas-default: #0d1117;
    --color-canvas-subtle: #161b22;
    --color-border-default: #30363d;
    --color-border-muted: #21262d;
    --color-neutral-muted: rgba(110,118,129,0.4);
    --color-accent-fg: #58a6ff;
    --color-accent-emphasis: #1f6feb;
    --color-attention-subtle: rgba(187,128,9,0.15);
    --color-danger-fg: #f85149;
  }
}

body {
  margin: 0;
  padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif;
  font-size: 16px;
  line-height: 1.6;
  color: var(--color-fg-default);
  background-color: var(--color-canvas-default);
  -webkit-font-smoothing: antialiased;
}

.markdown-body {
  max-width: 1012px;
  margin: 0 auto;
  padding: 32px 24px;
  word-wrap: break-word;
}

/* ── Headings ── */
.markdown-body h1,
.markdown-body h2,
.markdown-body h3,
.markdown-body h4,
.markdown-body h5,
.markdown-body h6 {
  margin-top: 24px;
  margin-bottom: 16px;
  font-weight: 600;
  line-height: 1.25;
}
.markdown-body h1 { font-size: 2em; padding-bottom: 0.3em; border-bottom: 1px solid var(--color-border-muted); }
.markdown-body h2 { font-size: 1.5em; padding-bottom: 0.3em; border-bottom: 1px solid var(--color-border-muted); }
.markdown-body h3 { font-size: 1.25em; }
.markdown-body h4 { font-size: 1em; }
.markdown-body h5 { font-size: 0.875em; }
.markdown-body h6 { font-size: 0.85em; color: var(--color-fg-muted); }

.markdown-body h1:hover .anchor,
.markdown-body h2:hover .anchor,
.markdown-body h3:hover .anchor,
.markdown-body h4:hover .anchor,
.markdown-body h5:hover .anchor,
.markdown-body h6:hover .anchor { text-decoration: none; }

/* ── Paragraphs & Links ── */
.markdown-body p {
  margin-top: 0;
  margin-bottom: 16px;
}
.markdown-body a {
  color: var(--color-accent-fg);
  text-decoration: none;
}
.markdown-body a:hover { text-decoration: underline; }

/* ── Lists ── */
.markdown-body ul,
.markdown-body ol {
  margin-top: 0;
  margin-bottom: 16px;
  padding-left: 2em;
}
.markdown-body li { margin-bottom: 0.25em; }
.markdown-body li > p { margin-top: 16px; }
.markdown-body li + li { margin-top: 0.25em; }

/* ── Task List ── */
.markdown-body ul.task-list { list-style: none; padding-left: 0; }
.markdown-body .task-list-item { padding-left: 1.5em; position: relative; }
.markdown-body .task-list-item input[type='checkbox'] {
  position: absolute; left: 0; top: 0.2em;
  margin: 0; width: 1.2em; height: 1.2em;
  accent-color: var(--color-accent-emphasis);
}

/* ── Blockquotes ── */
.markdown-body blockquote {
  margin: 0 0 16px 0;
  padding: 0 1em;
  color: var(--color-fg-muted);
  border-left: 0.25em solid var(--color-border-default);
}
.markdown-body blockquote > :first-child { margin-top: 0; }
.markdown-body blockquote > :last-child { margin-bottom: 0; }

/* ── Code ── */
.markdown-body code {
  padding: 0.2em 0.4em;
  margin: 0;
  font-family: ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, monospace;
  font-size: 85%;
  background-color: var(--color-neutral-muted);
  border-radius: 6px;
}
.markdown-body pre {
  margin: 0 0 16px 0;
  padding: 16px;
  overflow: auto;
  font-size: 85%;
  line-height: 1.45;
  background-color: var(--color-canvas-subtle);
  border-radius: 6px;
  border: 1px solid var(--color-border-muted);
}
.markdown-body pre code {
  padding: 0;
  margin: 0;
  background: transparent;
  border: 0;
  font-size: 100%;
  white-space: pre;
  word-break: normal;
}

/* ── Tables ── */
.markdown-body table {
  display: block;
  width: 100%;
  max-width: 100%;
  overflow: auto;
  border-spacing: 0;
  border-collapse: collapse;
  margin-bottom: 16px;
}
.markdown-body table th,
.markdown-body table td {
  padding: 6px 13px;
  border: 1px solid var(--color-border-default);
}
.markdown-body table th {
  font-weight: 600;
  background-color: var(--color-canvas-subtle);
}
.markdown-body table tr:nth-child(even) { background-color: var(--color-canvas-subtle); }
.markdown-body table tr { background-color: var(--color-canvas-default); }

/* ── Horizontal Rules ── */
.markdown-body hr {
  height: 0.25em;
  padding: 0;
  margin: 24px 0;
  background-color: var(--color-border-default);
  border: 0;
}

/* ── Images ── */
.markdown-body img {
  max-width: 100%;
  height: auto;
  border-style: none;
}
.markdown-body img[align=right] { padding-left: 20px; }
.markdown-body img[align=left] { padding-right: 20px; }

/* ── Footnotes ── */
.markdown-body .footnotes { font-size: 0.85em; color: var(--color-fg-muted); border-top: 1px solid var(--color-border-default); margin-top: 24px; padding-top: 16px; }
.markdown-body .footnotes ol { padding-left: 16px; }
.markdown-body .footnotes li { word-break: break-all; }

/* ── Definition Lists (Markdig) ── */
.markdown-body dl { margin-bottom: 16px; }
.markdown-body dt { font-weight: 600; margin-top: 8px; }
.markdown-body dd { padding-left: 16px; margin-bottom: 8px; }

/* ── Keyboard ── */
.markdown-body kbd {
  display: inline-block;
  padding: 3px 6px;
  font: 11px ui-monospace, SFMono-Regular, monospace;
  color: var(--color-fg-default);
  background-color: var(--color-canvas-subtle);
  border: 1px solid var(--color-border-muted);
  border-radius: 6px;
  box-shadow: inset 0 -1px 0 var(--color-border-muted);
}

/* ── Highlight / Mark ── */
.markdown-body mark { background-color: var(--color-attention-subtle); color: inherit; padding: 0.1em 0.2em; border-radius: 3px; }

/* ── Abbreviations ── */
.markdown-body abbr[title] { text-decoration: underline dotted; cursor: help; }

/* ── Superscript / Subscript ── */
.markdown-body sup, .markdown-body sub { font-size: 75%; line-height: 0; position: relative; vertical-align: baseline; }
.markdown-body sup { top: -0.5em; }
.markdown-body sub { bottom: -0.25em; }

/* ── Scrollbar (WebView2) ── */
::-webkit-scrollbar { width: 8px; height: 8px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: var(--color-border-default); border-radius: 4px; }
::-webkit-scrollbar-thumb:hover { background: var(--color-fg-muted); }
";
}
