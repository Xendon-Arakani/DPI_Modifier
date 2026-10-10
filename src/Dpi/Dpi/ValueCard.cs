using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Dpi;

internal sealed class ValueCard : Control
{
	public int Ordinal;

	public int Dpi;

	public bool IsSelected;

	public bool IsBefore;

	public bool IsBase;

	public ValueCard()
	{
		DoubleBuffered = true;
		Font = new Font("Segoe UI", 9f);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		float num = (float)base.Height / 70f;
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Color color = (IsSelected ? Color.FromArgb(66, 45, 39) : Color.FromArgb(26, 29, 36));
		Color color2 = (IsSelected ? Color.FromArgb(255, 112, 67) : Color.FromArgb(48, 53, 64));
		using (GraphicsPath path = Round(base.ClientRectangle, Math.Max(1, (int)(9f * num))))
		{
			using SolidBrush brush = new SolidBrush(color);
			using Pen pen = new Pen(color2, (IsSelected ? 2f : 1f) * num);
			e.Graphics.FillPath(brush, path);
			e.Graphics.DrawPath(pen, path);
		}
		Color foreColor = (IsSelected ? Color.White : (IsBefore ? Color.FromArgb(125, 132, 145) : Color.FromArgb(224, 228, 235)));
		string text = (IsBase ? "BASE" : ("ENTRY " + Ordinal));
		Color foreColor2 = (IsBase ? Color.FromArgb(60, 210, 143) : Color.FromArgb(125, 132, 145));
		using Font font = new Font("Segoe UI Semibold", 10f);
		using Font font2 = new Font("Segoe UI Semibold", 7f);
		TextRenderer.DrawText(e.Graphics, Dpi.ToString(), font, new Rectangle(0, (int)(15f * num), base.Width, (int)(21f * num)), foreColor, TextFormatFlags.HorizontalCenter);
		TextRenderer.DrawText(e.Graphics, text, font2, new Rectangle(0, (int)(40f * num), base.Width, (int)(15f * num)), foreColor2, TextFormatFlags.HorizontalCenter);
	}

	private static GraphicsPath Round(Rectangle bounds, int radius)
	{
		bounds.Width--;
		bounds.Height--;
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = radius * 2;
		graphicsPath.AddArc(bounds.Left, bounds.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
