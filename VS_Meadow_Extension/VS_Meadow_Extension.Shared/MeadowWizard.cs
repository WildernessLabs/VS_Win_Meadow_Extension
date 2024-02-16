using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MeadowTemplateWizard
{
	internal class MeadowWizard : IWizard
	{
		private UserInputForm inputForm;
		private string customMessage;

		public void BeforeOpeningFile(ProjectItem projectItem)
		{
		}

		public void ProjectFinishedGenerating(Project project)
		{
		}

		public void ProjectItemFinishedGenerating(ProjectItem projectItem)
		{
		}

		public void RunFinished()
		{
		}

		public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
		{
			try
			{
				// Display a form to the user. The form collects
				// input for the custom message.
				inputForm = new UserInputForm();
				inputForm.ShowDialog();

				customMessage = UserInputForm.CustomMessage;

				// Add custom parameters.
				replacementsDictionary.Add("$custommessage$",
					customMessage);
			}
			catch (Exception ex)
			{
                MessageBox.Show(ex.ToString());
			}
		}

		public bool ShouldAddProjectItem(string filePath)
		{
			return true;
		}
	}


}
public partial class UserInputForm : Form
{
	private static string customMessage;
	private TextBox textBox1;
	private Button button1;

	public UserInputForm()
	{
		this.Size = new System.Drawing.Size(155, 265);

		button1 = new Button();
		button1.Location = new System.Drawing.Point(90, 25);
		button1.Size = new System.Drawing.Size(50, 25);
		button1.Click += button1_Click;
		this.Controls.Add(button1);

		textBox1 = new TextBox();
		textBox1.Location = new System.Drawing.Point(10, 25);
		textBox1.Size = new System.Drawing.Size(70, 20);
		this.Controls.Add(textBox1);
	}
	public static string CustomMessage
	{
		get
		{
			return customMessage;
		}
		set
		{
			customMessage = value;
		}
	}
	private void button1_Click(object sender, EventArgs e)
	{
		customMessage = textBox1.Text;
		this.Close();
	}
}