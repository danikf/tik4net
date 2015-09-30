using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using tik4net.entitygenerator;

namespace tik4net.entityWikiImporter
{
    public static class HtmlParserExtensions
    {
        public static string ParseEntityPath(this HtmlDocument doc)
        {
            string articleHeader = doc.DocumentNode.SelectSingleNode("//h1[@id='firstHeading']/span").InnerText;
            var items = articleHeader.Split(':'); //Manual:Interface/Bonding
            return items[1].ToLower();
        }

        public static HtmlNode GetParagraphNode(this HtmlDocument doc, params string[] paragraphNames)
        {
            foreach (var paraName in paragraphNames)
            {
                var result = doc.DocumentNode.SelectSingleNode("//h2[span[@class='mw-headline' and text()='" + paraName + "']]"); ;
                if (result == null)
                    result = doc.DocumentNode.SelectSingleNode("//h3[span[@class='mw-headline' and text()='" + paraName + "']]"); ;

                if (result != null)
                    return result;
            }

            return null;
        }

        public static bool IsParagraphNode(this HtmlNode node)
        {
            return (string.Equals(node.Name, "h2", StringComparison.OrdinalIgnoreCase)  || string.Equals(node.Name, "h3", StringComparison.OrdinalIgnoreCase))
                && node.SelectSingleNode("span[@class='mw-headline']") != null;
        }

        public static IEnumerable<string> GetTextOfParagraph(this HtmlDocument doc, params string[] paragraphNames)
        {
            var paragraphHeader = doc.GetParagraphNode(paragraphNames);
            if (paragraphHeader != null)
            {
                var node = paragraphHeader.NextSibling;
                while (node != null && !node.IsParagraphNode())
                {
                    if (node.Name == "p" && node.Attributes.Count == 0)
                        yield return node.InnerText;

                    node = node.NextSibling;
                }
            }
        }

        public static HtmlNode GetNextSiblingOfType(this HtmlNode node, string nodeName)
        {
            var result = node.NextSibling;
            while (result != null && !result.IsParagraphNode())
            {
                if (result.Name == nodeName)
                    return result;
                result = result.NextSibling;
            }

            return null;
        }

        public static IEnumerable<ParsedProperty> ParsePropertyTable(this HtmlNode tableNode, bool isReadOnly)
        {
            var rows = tableNode.SelectNodes("//tr[td]");

            foreach(var row in rows)
            {
                var cells = row.SelectNodes("td");

                string fieldText = cells[0].InnerText; //l2mtu(integer; Default:                
                string fieldName;
                string fieldType;
                string defaultValue;
                ParseFieldText(fieldText, isReadOnly, out fieldName, out fieldType, out defaultValue);
                string description = cells[1].InnerText;
                string propName = GeneratorHelper.Camelize(fieldName);
                bool isMandatory = GeneratorHelper.DetermineFieldMandatory(fieldName, string.Empty);                

                yield return new ParsedProperty(propName, fieldName, description, fieldType, isReadOnly, isMandatory, defaultValue);
            }
        }

        private static Regex fieldRegex = new Regex(@"^(?<FLD>[\w\-]+)\s*\((?<TYPE>.+)\s*;\s*Default:\s*(?<DEFAULT>.*)\)$", RegexOptions.IgnoreCase);
        private static Regex fieldRegexFallback = new Regex(@"^(?<FLD>[\w\-]+)\s*\(.*Default:\s*(?<DEFAULT>.*)\)$", RegexOptions.IgnoreCase);
        private static Regex fieldRegexFallback2 = new Regex(@"^(?<FLD>[\w\-]+)\s*\((?<TYPE>.*)\s*(?<DEFAULT>.*)\)$", RegexOptions.IgnoreCase);
        private static void ParseFieldText(string fieldText, bool isReadOnly, out string fieldName, out string fieldType, out string defaultValue)
        {
            Match match = fieldRegex.Match(fieldText);
            if (!match.Success)
                match = fieldRegexFallback.Match(fieldText);
            if (!match.Success)
                match = fieldRegexFallback2.Match(fieldText);

            if (match.Success)
            {
                fieldName = match.Groups["FLD"].Value;
                fieldType = GeneratorHelper.DetermineFieldTypeFromDocumentation(match.Groups["TYPE"].Value, fieldName, isReadOnly);
                defaultValue = match.Groups["DEFAULT"].Value;  
            }
            else
            {
                fieldName = "Unknown";
                fieldType = "UNKNOWN /*" + fieldText + "*/";
                defaultValue = null;
            }
        }
    }
}
