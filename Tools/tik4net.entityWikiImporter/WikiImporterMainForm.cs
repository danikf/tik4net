using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using HtmlAgilityPack;
using tik4net.entitygenerator;

namespace tik4net.entityWikiImporter
{
    public partial class WikiImporterMainForm : Form
    {
        public WikiImporterMainForm()
        {
            InitializeComponent();
        }

        private static IEnumerable<ParsedProperty> ParsePropertiesFromHtml(string html, bool isReadOnly)
        {
            if (string.IsNullOrWhiteSpace(html))
                return new List<ParsedProperty>(); //empty

            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var rootDoc = doc.DocumentNode.FirstChild;
            if (rootDoc.Name == "table")
            {
                return rootDoc.ParsePropertyTable(isReadOnly);
            }
            else
            {
                throw new Exception("Unsuported property format: " + rootDoc.Name);
            }
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            var properties = ParsePropertiesFromHtml(tbProperties.Text, false).ToList();
            var roProperties = ParsePropertiesFromHtml(tbROProperties.Text, true).ToList();

            tbSourceCode.Text = EntityCodeGenerator.Generate(tbEntityPath.Text, tbDescription.Text, false, 
                new List<ParsedProperty> { new ParsedProperty("Id", ".id", ".id: primary key of row", "string", true, true, null)}                
                .Concat(properties)
                .Concat(roProperties));
            tbSourceCode.SelectAll();
            tbSourceCode.Focus();            
        }


        private void btnLoadAndResolve_Click(object sender, EventArgs e)
        {
            tbEntityPath.Text = "";
            tbProperties.Text = "";
            tbROProperties.Text = "";
            tbDescription.Text = "";

            HtmlWeb web = new HtmlWeb();
            HtmlAgilityPack.HtmlDocument doc = web.Load(tbWikiUrl.Text);
            //var rootContentDiv = doc.DocumentNode.SelectSingleNode("/html/body/div[@id='content']");
            //var documentationContentNode = rootContentDiv.SelectSingleNode("div[@id='bodyContent']/div[@id='mw-content-text']");
            
            tbEntityPath.Text = doc.ParseEntityPath(); //entity path from article header
            tbDescription.Lines = doc.GetTextOfParagraph("Summary").ToArray(); //description from text of "Summary" paragraph

            var propertiesPara = doc.GetParagraphNode("Properties", "Property Description"); //properties from "Properties" or "Property Description" paragraph
            if (propertiesPara != null)
            {
                var propertiesTable = propertiesPara.GetNextSiblingOfType("table");
                tbProperties.Text = propertiesTable.OuterHtml;
            }

            var roPropertiesPara = doc.GetParagraphNode("Read-only properties", "Read only properties"); // R/O properties from "Read-only properties" or "Read only properties" paragraph
            if (roPropertiesPara != null)
            {
                var roPropertiesTable = roPropertiesPara.GetNextSiblingOfType("table");
                tbROProperties.Text = roPropertiesTable.OuterHtml;
                //var properties = roPropertiesTable.ParsePropertyTable(true).ToArray();
            }
        }
    }
}
