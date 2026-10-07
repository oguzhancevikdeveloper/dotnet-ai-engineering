using HtmlAgilityPack;
using System.Xml;

namespace PgVectorVectorSearch.API.Services;

public class BlogService
{
    private readonly HttpClient _httpClient;
    public BlogService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<List<string>> GetSitemapAsync()
    {
        const string sitemapUrl = "https://seniorcarquotes.com/post-sitemap.xml";
        string xmlContent = await _httpClient.GetStringAsync(sitemapUrl);
        XmlDocument xmlDocument = new();
        xmlDocument.LoadXml(xmlContent);
        var nodes = xmlDocument.GetElementsByTagName("loc");

        return nodes.Cast<XmlNode>().Select(node => node.InnerText).ToList();
    }

    public async Task<(string title, string content)> GetTitleAndContentAsync(string url)
    {
        string htmlContent = await _httpClient.GetStringAsync(url);

        HtmlDocument htmlDocument = new();
        htmlDocument.LoadHtml(htmlContent);

        string title = htmlDocument.DocumentNode
            .SelectSingleNode("//title")
            ?.InnerText
            ?.Trim() ?? "";

        string content = htmlDocument.DocumentNode
            .SelectSingleNode("//body")
            ?.InnerText
            ?.Trim() ?? "";

        return (title: title, content: content);
    }
}