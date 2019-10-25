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
    public partial class EntityCodeGenaratorMainForm : Form
    {
        public EntityCodeGenaratorMainForm()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            cbTikApi.DataSource = Enum.GetValues(typeof(TikConnectionType));

            tbHost.Text = ConfigurationManager.AppSettings["host"];
            tbUser.Text = ConfigurationManager.AppSettings["user"];
            tbPass.Text = ConfigurationManager.AppSettings["pass"];

            cbTikApi.Text = ConfigurationManager.AppSettings["connectionType"];
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            btnGenerate.Enabled = false;
            try
            {
                using (ITikConnection connection = ConnectionFactory.CreateConnection((TikConnectionType)Enum.Parse(typeof(TikConnectionType), cbTikApi.SelectedValue.ToString())))
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
            List<ParsedProperty> properties = new List<ParsedProperty>();
            foreach (ITikReSentence sentence in tikReSentences)
            {
                foreach (var propPair in sentence.Words)
                {
                    if (!properties.Any(prop => prop.FieldName == propPair.Key)) //union over all field names from all rows
                    {
                        string fieldName = propPair.Key;
                        string fielValue = propPair.Value;

                        string propName = GeneratorHelper.Camelize(fieldName);
                        string propType = GeneratorHelper.DetermineFieldType(fieldName, fielValue);
                        bool isReadOnly = GeneratorHelper.DetermineFieldReadOnly(fieldName, fielValue);
                        bool isMandatory = GeneratorHelper.DetermineFieldMandatory(fieldName, fielValue);

                        properties.Add(new ParsedProperty(propName, fieldName, "", propType, isReadOnly, isMandatory, null));
                    }
                }
            }

            return EntityCodeGenerator.Generate(entityPath, "", includeDetails, properties);
        }
    }
}
