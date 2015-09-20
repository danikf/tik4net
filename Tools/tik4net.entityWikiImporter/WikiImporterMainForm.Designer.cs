namespace tik4net.entityWikiImporter
{
    partial class WikiImporterMainForm
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
            System.Windows.Forms.Label label3;
            System.Windows.Forms.Label label4;
            this.tbDescription = new System.Windows.Forms.TextBox();
            this.btnGenerate = new System.Windows.Forms.Button();
            this.tbWikiUrl = new System.Windows.Forms.TextBox();
            this.btnLoadAndResolve = new System.Windows.Forms.Button();
            this.tbEntityPath = new System.Windows.Forms.TextBox();
            this.tbProperties = new System.Windows.Forms.TextBox();
            this.tbROProperties = new System.Windows.Forms.TextBox();
            this.tbSourceCode = new System.Windows.Forms.TextBox();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            label4 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 15);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(88, 13);
            label1.TabIndex = 6;
            label1.Text = "Mikrotik WIKI url:";
            // 
            // tbDescription
            // 
            this.tbDescription.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbDescription.Location = new System.Drawing.Point(4, 47);
            this.tbDescription.Multiline = true;
            this.tbDescription.Name = "tbDescription";
            this.tbDescription.Size = new System.Drawing.Size(849, 50);
            this.tbDescription.TabIndex = 3;
            // 
            // btnGenerate
            // 
            this.btnGenerate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGenerate.Location = new System.Drawing.Point(731, 490);
            this.btnGenerate.Name = "btnGenerate";
            this.btnGenerate.Size = new System.Drawing.Size(130, 23);
            this.btnGenerate.TabIndex = 4;
            this.btnGenerate.Text = "2) Generate code";
            this.btnGenerate.UseVisualStyleBackColor = true;
            this.btnGenerate.Click += new System.EventHandler(this.btnGenerate_Click);
            // 
            // tbWikiUrl
            // 
            this.tbWikiUrl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbWikiUrl.Location = new System.Drawing.Point(123, 12);
            this.tbWikiUrl.Name = "tbWikiUrl";
            this.tbWikiUrl.Size = new System.Drawing.Size(346, 20);
            this.tbWikiUrl.TabIndex = 5;
            this.tbWikiUrl.Text = "http://wiki.mikrotik.com/wiki/Manual:Interface";
            // 
            // btnLoadAndResolve
            // 
            this.btnLoadAndResolve.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadAndResolve.Location = new System.Drawing.Point(595, 490);
            this.btnLoadAndResolve.Name = "btnLoadAndResolve";
            this.btnLoadAndResolve.Size = new System.Drawing.Size(130, 23);
            this.btnLoadAndResolve.TabIndex = 7;
            this.btnLoadAndResolve.Text = "1) Load + Resolve";
            this.btnLoadAndResolve.UseVisualStyleBackColor = true;
            this.btnLoadAndResolve.Click += new System.EventHandler(this.btnLoadAndResolve_Click);
            // 
            // tbEntityPath
            // 
            this.tbEntityPath.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbEntityPath.Location = new System.Drawing.Point(524, 12);
            this.tbEntityPath.Name = "tbEntityPath";
            this.tbEntityPath.Size = new System.Drawing.Size(337, 20);
            this.tbEntityPath.TabIndex = 8;
            // 
            // tbProperties
            // 
            this.tbProperties.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbProperties.Location = new System.Drawing.Point(4, 117);
            this.tbProperties.Multiline = true;
            this.tbProperties.Name = "tbProperties";
            this.tbProperties.Size = new System.Drawing.Size(849, 97);
            this.tbProperties.TabIndex = 9;
            // 
            // tbROProperties
            // 
            this.tbROProperties.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbROProperties.Location = new System.Drawing.Point(4, 233);
            this.tbROProperties.Multiline = true;
            this.tbROProperties.Name = "tbROProperties";
            this.tbROProperties.Size = new System.Drawing.Size(849, 98);
            this.tbROProperties.TabIndex = 10;
            // 
            // tbSourceCode
            // 
            this.tbSourceCode.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbSourceCode.Location = new System.Drawing.Point(4, 362);
            this.tbSourceCode.Multiline = true;
            this.tbSourceCode.Name = "tbSourceCode";
            this.tbSourceCode.Size = new System.Drawing.Size(849, 122);
            this.tbSourceCode.TabIndex = 11;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(12, 31);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(63, 13);
            label2.TabIndex = 12;
            label2.Text = "Description:";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(12, 101);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(175, 13);
            label3.TabIndex = 13;
            label3.Text = "Html with properties (table or ul tag):";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(1, 217);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(199, 13);
            label4.TabIndex = 14;
            label4.Text = "Html with R/O properties (table or ul tag):";
            // 
            // WikiImporterMainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(865, 518);
            this.Controls.Add(label4);
            this.Controls.Add(label3);
            this.Controls.Add(label2);
            this.Controls.Add(this.tbSourceCode);
            this.Controls.Add(this.tbROProperties);
            this.Controls.Add(this.tbProperties);
            this.Controls.Add(this.tbEntityPath);
            this.Controls.Add(this.btnLoadAndResolve);
            this.Controls.Add(label1);
            this.Controls.Add(this.tbWikiUrl);
            this.Controls.Add(this.btnGenerate);
            this.Controls.Add(this.tbDescription);
            this.Name = "WikiImporterMainForm";
            this.Text = "Import from mikroti wiki";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox tbDescription;
        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.TextBox tbWikiUrl;
        private System.Windows.Forms.Button btnLoadAndResolve;
        private System.Windows.Forms.TextBox tbEntityPath;
        private System.Windows.Forms.TextBox tbProperties;
        private System.Windows.Forms.TextBox tbROProperties;
        private System.Windows.Forms.TextBox tbSourceCode;
    }
}

