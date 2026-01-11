namespace Liza.Infrastructure.Analysis;

using System.Text.RegularExpressions;
using System.Net;
using Liza.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for extracting and ranking keywords from video content
/// Uses TF-IDF scoring and tracks keyword sources
/// </summary>
public interface IKeywordExtractionService
{
    Task<List<ExtractedKeyword>> ExtractAsync(string seedKeyword, List<EnrichedVideo> videos, int limit = 50);
}

public class KeywordExtractionService : IKeywordExtractionService
{
    private readonly ILogger<KeywordExtractionService> _logger;
    
    // Common English stop words to filter out
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
        "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does",
        "did", "will", "would", "could", "should", "may", "might", "must", "shall", "can",
        "this", "that", "these", "those", "i", "you", "he", "she", "it", "we", "they", "me",
        "him", "her", "us", "them", "my", "your", "his", "its", "our", "their", "what", "which",
        "who", "whom", "how", "when", "where", "why", "all", "each", "every", "both", "few",
        "more", "most", "other", "some", "such", "no", "nor", "not", "only", "own", "same",
        "so", "than", "too", "very", "just", "also", "now", "here", "there", "then", "if",
        "about", "into", "through", "during", "before", "after", "above", "below", "from",
        "up", "down", "out", "off", "over", "under", "again", "further", "once", "any",
        "like", "get", "got", "make", "made", "know", "think", "see", "come", "want", "use",
        "find", "give", "tell", "try", "leave", "call", "keep", "let", "put", "seem", "help",
        "show", "hear", "play", "run", "move", "live", "believe", "hold", "bring", "happen",
        "write", "provide", "sit", "stand", "lose", "pay", "meet", "include", "continue",
        "set", "learn", "change", "lead", "understand", "watch", "follow", "stop", "create",
        "speak", "read", "allow", "add", "spend", "grow", "open", "walk", "win", "offer",
        "remember", "love", "consider", "appear", "buy", "wait", "serve", "die", "send",
        "expect", "build", "stay", "fall", "cut", "reach", "kill", "remain", "video", "videos",
        // HTML/Web artifacts
        "www", "com", "http", "https", "href", "quot", "amp", "nbsp", "org", "net", "html",
        "youtube", "youtu", "watch", "channel", "subscribe", "link", "click", "url", "bit", "ly"
    };
    
    // Regex patterns for filtering
    private static readonly Regex UrlPattern = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntityPattern = new(@"&\w+;|&#\d+;", RegexOptions.Compiled);

    public KeywordExtractionService(ILogger<KeywordExtractionService> logger)
    {
        _logger = logger;
    }

    public Task<List<ExtractedKeyword>> ExtractAsync(string seedKeyword, List<EnrichedVideo> videos, int limit = 50)
    {
        _logger.LogInformation("Extracting keywords from {VideoCount} videos", videos.Count);
        
        var keywordData = new Dictionary<string, KeywordSourceBreakdown>(StringComparer.OrdinalIgnoreCase);
        var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var video in videos)
        {
            var documentKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Extract from title
            var titleKeywords = ExtractKeywords(video.Details.Title);
            foreach (var kw in titleKeywords)
            {
                documentKeywords.Add(kw);
                UpdateSourceCount(keywordData, kw, source => source with { TitleCount = source.TitleCount + 1 });
            }
            
            // Extract from description
            var descKeywords = ExtractKeywords(video.Details.Description);
            foreach (var kw in descKeywords)
            {
                documentKeywords.Add(kw);
                UpdateSourceCount(keywordData, kw, source => source with { DescriptionCount = source.DescriptionCount + 1 });
            }
            
            // Extract from tags
            foreach (var tag in video.Details.Tags)
            {
                var normalizedTag = NormalizeKeyword(tag);
                if (!string.IsNullOrEmpty(normalizedTag) && normalizedTag.Length > 2)
                {
                    documentKeywords.Add(normalizedTag);
                    UpdateSourceCount(keywordData, normalizedTag, source => source with { TagCount = source.TagCount + 1 });
                }
            }
            
            // Extract from transcript
            if (video.Transcript != null)
            {
                var transcriptKeywords = ExtractKeywords(video.Transcript.FullText);
                foreach (var kw in transcriptKeywords)
                {
                    documentKeywords.Add(kw);
                    UpdateSourceCount(keywordData, kw, source => source with { TranscriptCount = source.TranscriptCount + 1 });
                }
            }
            
            // Extract from comments
            foreach (var comment in video.Comments.Take(50))
            {
                var commentKeywords = ExtractKeywords(comment.Text);
                foreach (var kw in commentKeywords)
                {
                    documentKeywords.Add(kw);
                    UpdateSourceCount(keywordData, kw, source => source with { CommentCount = source.CommentCount + 1 });
                }
            }
            
            // Update document frequency for TF-IDF
            foreach (var kw in documentKeywords)
            {
                documentFrequency[kw] = documentFrequency.GetValueOrDefault(kw) + 1;
            }
        }
        
        // Calculate TF-IDF scores
        var totalDocs = Math.Max(videos.Count, 1);
        var results = keywordData
            .Select(kv =>
            {
                var totalCount = kv.Value.TitleCount + kv.Value.DescriptionCount + 
                                 kv.Value.TagCount + kv.Value.TranscriptCount + kv.Value.CommentCount;
                var docFreq = documentFrequency.GetValueOrDefault(kv.Key, 1);
                var idf = Math.Log((double)totalDocs / docFreq);
                var tfIdf = totalCount * idf;
                
                return new ExtractedKeyword
                {
                    Keyword = kv.Key,
                    TotalCount = totalCount,
                    TfIdfScore = Math.Round(tfIdf, 2),
                    Sources = kv.Value
                };
            })
            .OrderByDescending(k => k.TfIdfScore)
            .Take(limit)
            .ToList();
        
        _logger.LogInformation("Extracted {KeywordCount} unique keywords", results.Count);
        return Task.FromResult(results);
    }

    private List<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        
        // Clean the text first
        var cleanedText = CleanText(text);
        
        // Extract 1-grams and 2-grams
        var words = Regex.Split(cleanedText.ToLowerInvariant(), @"[^\w]+")
            .Where(w => !string.IsNullOrEmpty(w) && w.Length > 2 && !StopWords.Contains(w) && IsValidKeyword(w))
            .ToList();
        
        var keywords = new List<string>();
        
        // Single words
        keywords.AddRange(words);
        
        // 2-grams (bigrams)
        for (int i = 0; i < words.Count - 1; i++)
        {
            var bigram = $"{words[i]} {words[i + 1]}";
            keywords.Add(bigram);
        }
        
        // 3-grams (trigrams) for longer phrases
        for (int i = 0; i < words.Count - 2; i++)
        {
            var trigram = $"{words[i]} {words[i + 1]} {words[i + 2]}";
            keywords.Add(trigram);
        }
        
        return keywords;
    }

    /// <summary>
    /// Clean text by removing HTML artifacts, URLs, and decoding entities
    /// </summary>
    private string CleanText(string text)
    {
        // Remove URLs
        text = UrlPattern.Replace(text, " ");
        
        // Remove HTML tags
        text = HtmlTagPattern.Replace(text, " ");
        
        // Decode HTML entities (e.g., &quot; -> ")
        text = WebUtility.HtmlDecode(text);
        
        // Remove any remaining HTML entity patterns
        text = HtmlEntityPattern.Replace(text, " ");
        
        return text;
    }

    /// <summary>
    /// Check if a keyword is valid (not a number, not too short, not a URL fragment)
    /// </summary>
    private bool IsValidKeyword(string word)
    {
        // Skip pure numbers
        if (int.TryParse(word, out _)) return false;
        
        // Skip very short words
        if (word.Length < 3) return false;
        
        // Skip words that look like URL fragments or codes
        if (word.StartsWith("http") || word.Contains("://")) return false;
        
        // Skip hex-like strings (e.g., "ff0000", "a1b2c3")
        if (Regex.IsMatch(word, @"^[0-9a-f]{6,}$")) return false;
        
        return true;
    }

    private string NormalizeKeyword(string keyword)
    {
        return Regex.Replace(keyword.ToLowerInvariant().Trim(), @"[^\w\s]", "");
    }

    private void UpdateSourceCount(
        Dictionary<string, KeywordSourceBreakdown> data, 
        string keyword, 
        Func<KeywordSourceBreakdown, KeywordSourceBreakdown> update)
    {
        if (!data.TryGetValue(keyword, out var current))
        {
            current = new KeywordSourceBreakdown();
        }
        data[keyword] = update(current);
    }
}

