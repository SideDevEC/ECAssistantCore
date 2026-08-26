using System;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Converts raw HTML into structured plain text, preserving block-level
/// boundaries (paragraphs, headings, lists, line breaks) so the output
/// is readable by both humans and LLMs.
/// </summary>
public interface IHtmlTextConverter
{
    /// <summary>
    /// Convert HTML source into plain text with line breaks preserved.
    /// Script/style tags are removed; block-level tags produce newlines.
    /// </summary>
    /// <param name="html">Raw HTML string</param>
    /// <returns>Plain text with structural line breaks</returns>
    string Convert(string html);
}