namespace tik4net.entitygenerator
{
    partial class EntityCodeGenaratorMainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.Label label1;
            System.Windows.Forms.Label label2;
            this.btnGenerate = new System.Windows.Forms.Button();
            this.tbPath = new System.Windows.Forms.TextBox();
            this.tbSourceCode = new System.Windows.Forms.TextBox();
            this.cbIncludeDetails = new System.Windows.Forms.CheckBox();
            this.tbHost = new System.Windows.Forms.TextBox();
            this.tbUser = new System.Windows.Forms.TextBox();
            this.tbPass = new System.Windows.Forms.TextBox();
            this.cbExecuteAsync = new System.Windows.Forms.CheckBox();
            this.tbParameters = new System.Windows.Forms.TextBox();
            this.cbTikApi = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 42);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(57, 13);
            label1.TabIndex = 3;
            label1.Text = "Entity path";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(12, 15);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(81, 13);
            label2.TabIndex = 8;
            label2.Text = "Host/user/pass";
            // 
            // btnGenerate
            // 
            this.btnGenerate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGenerate.Location = new System.Drawing.Point(619, 405);
            this.btnGenerate.Name = "btnGenerate";
            this.btnGenerate.Size = new System.Drawing.Size(130, 23);
            this.btnGenerate.TabIndex = 0;
            this.btnGenerate.Text = "Load and generate";
            this.btnGenerate.UseVisualStyleBackColor = true;
            this.btnGenerate.Click += new System.EventHandler(this.btnGenerate_Click);
            // 
            // tbPath
            // 
            this.tbPath.Location = new System.Drawing.Point(75, 39);
            this.tbPath.Name = "tbPath";
            this.tbPath.Size = new System.Drawing.Size(157, 20);
            this.tbPath.TabIndex = 1;
            this.tbPath.Text = "/print";
            // 
            // tbSourceCode
            // 
            this.tbSourceCode.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbSourceCode.Location = new System.Drawing.Point(12, 88);
            this.tbSourceCode.Multiline = true;
            this.tbSourceCode.Name = "tbSourceCode";
            this.tbSourceCode.Size = new System.Drawing.Size(737, 311);
            this.tbSourceCode.TabIndex = 2;
            // 
            // cbIncludeDetails
            // 
            this.cbIncludeDetails.AutoSize = true;
            this.cbIncludeDetails.Checked = true;
            this.cbIncludeDetails.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbIncludeDetails.Location = new System.Drawing.Point(15, 65);
            this.cbIncludeDetails.Name = "cbIncludeDetails";
            this.cbIncludeDetails.Size = new System.Drawing.Size(94, 17);
            this.cbIncludeDetails.TabIndex = 4;
            this.cbIncludeDetails.Text = "Include details";
            this.cbIncludeDetails.UseVisualStyleBackColor = true;
            // 
            // tbHost
            // 
            this.tbHost.Location = new System.Drawing.Point(246, 13);
            this.tbHost.Name = "tbHost";
            this.tbHost.Size = new System.Drawing.Size(172, 20);
            this.tbHost.TabIndex = 5;
            // 
            // tbUser
            // 
            this.tbUser.Location = new System.Drawing.Point(425, 13);
            this.tbUser.Name = "tbUser";
            this.tbUser.Size = new System.Drawing.Size(179, 20);
            this.tbUser.TabIndex = 6;
            // 
            // tbPass
            // 
            this.tbPass.Location = new System.Drawing.Point(611, 12);
            this.tbPass.Name = "tbPass";
            this.tbPass.Size = new System.Drawing.Size(138, 20);
            this.tbPass.TabIndex = 7;
            // 
            // cbExecuteAsync
            // 
            this.cbExecuteAsync.AutoSize = true;
            this.cbExecuteAsync.Location = new System.Drawing.Point(136, 65);
            this.cbExecuteAsync.Name = "cbExecuteAsync";
            this.cbExecuteAsync.Size = new System.Drawing.Size(96, 17);
            this.cbExecuteAsync.TabIndex = 9;
            this.cbExecuteAsync.Text = "Execute async";
            this.cbExecuteAsync.UseVisualStyleBackColor = true;
            // 
            // tbParameters
            // 
            this.tbParameters.AccessibleRole = System.Windows.Forms.AccessibleRole.ScrollBar;
            this.tbParameters.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbParameters.Location = new System.Drawing.Point(246, 39);
            this.tbParameters.Name = "tbParameters";
            this.tbParameters.Size = new System.Drawing.Size(503, 20);
            this.tbParameters.TabIndex = 10;
            // 
            // comboBox1
            // 
            this.cbTikApi.FormattingEnabled = true;
            this.cbTikApi.Location = new System.Drawing.Point(276, 63);
            this.cbTikApi.Name = "comboBox1";
            this.cbTikApi.Size = new System.Drawing.Size(121, 21);
            this.cbTikApi.TabIndex = 11;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(243, 66);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(27, 13);
            this.label3.TabIndex = 12;
            this.label3.Text = "API:";
            // 
            // EntityCodeGenaratorMainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(761, 440);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.cbTikApi);
            this.Controls.Add(this.tbParameters);
            this.Controls.Add(this.cbExecuteAsync);
            this.Controls.Add(label2);
            this.Controls.Add(this.tbPass);
            this.Controls.Add(this.tbUser);
            this.Controls.Add(this.tbHost);
            this.Controls.Add(this.cbIncludeDetails);
            this.Controls.Add(label1);
            this.Controls.Add(this.tbSourceCode);
            this.Controls.Add(this.tbPath);
            this.Controls.Add(this.btnGenerate);
            this.Name = "EntityCodeGenaratorMainForm";
            this.Text = "Entity source code generator";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.TextBox tbPath;
        private System.Windows.Forms.TextBox tbSourceCode;
        private System.Windows.Forms.CheckBox cbIncludeDetails;
        private System.Windows.Forms.TextBox tbHost;
        private System.Windows.Forms.TextBox tbUser;
        private System.Windows.Forms.TextBox tbPass;
        private System.Windows.Forms.CheckBox cbExecuteAsync;
        private System.Windows.Forms.TextBox tbParameters;
        private System.Windows.Forms.ComboBox cbTikApi;
        private System.Windows.Forms.Label label3;
    }
}

