using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace tik4net.entitygenerator
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            tbHost.Text = ConfigurationManager.AppSettings["host"];
            tbUser.Text = ConfigurationManager.AppSettings["user"];
            tbPass.Text = ConfigurationManager.AppSettings["pass"];
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            btnGenerate.Enabled = false;
            try
            {
                using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
                {
                    connection.Open(tbHost.Text, tbUser.Text, tbPass.Text);

                    var cmd = connection.CreateCommand(tbPath.Text);
                    if (cbIncludeDetails.Checked)
                        cmd.AddParameter("detail", "");
                    if (!string.IsNullOrWhiteSpace(tbParameters.Text))
                        cmd.AddParameterAndValues(tbParameters.Text.Split(';', '|'));

                    List<ITikReSentence> rows;
                    if (!cbExecuteAsync.Checked)
                        rows = cmd.ExecuteList().ToList();
                    else
                        rows = cmd.ExecuteListWithDuration(10).ToList();

                    if (rows.Any())
                    {
                        tbSourceCode.Text = Generate(tbPath.Text, cbIncludeDetails.Checked, rows);
                        tbSourceCode.SelectAll();
                        tbSourceCode.Focus();
                    }
                    else
                        MessageBox.Show("Empty response");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            btnGenerate.Enabled = true;
        }

        private static string Generate(string entityPath, bool includeDetails, IEnumerable<ITikReSentence> tikReSentences)
        {
            StringBuilder source = new StringBuilder();
            source.AppendLine(string.Format("\t[TikEntity(\"{0}\"{1})]", entityPath, includeDetails ? ", IncludeDetails = true" : ""));
            source.AppendLine(string.Format("\tpublic class {0}", "ENTITY_NAME"));
            source.AppendLine("\t{");

            Dictionary<string, string> words = new Dictionary<string, string>();
            foreach (ITikReSentence sentence in tikReSentences)
            {
                foreach (var propPair in sentence.Words)
                {
                    if (!words.ContainsKey(propPair.Key)) //union over all field names
                        words.Add(propPair.Key, propPair.Value);
                }
            }

            foreach (var propPair in words)
            {
                GenerateProperty(propPair.Key, propPair.Value, source);
            }

            source.AppendLine("\t}");

            return source.ToString();
        }

        private static void GenerateProperty(string name, string value, StringBuilder source)
        {
            string propName = GeneratorHelper.Camelize(name);
            string propType = GeneratorHelper.DetermineFieldType(name, value);
            bool isReadOnly = GeneratorHelper.DetermineFieldReadOnly(name, value);
            bool isMandatory = GeneratorHelper.DetermineFieldMandatory(name, value);

            List<string> attrParams = new List<string>();
            attrParams.Add(string.Format("\"{0}\"", name));
            if (isReadOnly)
                attrParams.Add("IsReadOnly = true");
            if (isMandatory)
                attrParams.Add("IsMandatory = true");

            source.AppendLine(string.Format("\t\t[TikProperty({0})]", string.Join(", ", attrParams)));
            source.AppendLine(string.Format("\t\tpublic {0} {1} *< get; {2}set; >*", propType, propName, (isReadOnly ? "private " : "")).Replace("*<", "{").Replace(">*", "}"));
            source.AppendLine();
        }
    }
}
