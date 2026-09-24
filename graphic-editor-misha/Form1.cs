using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace graphic_editor_misha
{
    public partial class Form1 : Form
    {
        // Картинка, которую редактируем. Всё рисование идёт в неё, а не прямо на экран.
        private Bitmap bmp;

        // История для Undo/Redo: копии картинки до изменения.
        private Stack<Bitmap> undoStack = new Stack<Bitmap>();
        private Stack<Bitmap> redoStack = new Stack<Bitmap>();

        private string currentFile;              // путь к открытому файлу (null — ещё не сохранён)
        private ToolStripButton currentTool;     // кнопка выбранного инструмента
        private Color lineColor = Color.Black;   // цвет контура
        private Color fillColor = Color.Yellow;  // цвет заливки

        private bool drawing;                    // зажата ли левая кнопка мыши
        private Point startPoint;                // где нажали кнопку мыши
        private Point lastPoint;                 // предыдущая точка карандаша
        private Point endPoint;                  // текущая точка при рисовании фигуры
        private Rectangle selection;             // выделенная область (пустая — ничего не выделено)

        public Form1()
        {
            InitializeComponent();
            currentTool = btnPencil;
            cmbWidth.SelectedIndex = 1;
            UpdateColorButtons();
            NewImage();
        }

        // ---------- Вспомогательные методы ----------

        private void NewImage()
        {
            Bitmap b = new Bitmap(800, 500);
            using (Graphics g = Graphics.FromImage(b))
                g.Clear(Color.White);
            undoStack.Clear();
            redoStack.Clear();
            currentFile = null;
            SetBitmap(b);
        }

        // Делает картинку текущей и подгоняет под неё размер холста.
        private void SetBitmap(Bitmap b)
        {
            bmp = b;
            canvas.Size = bmp.Size;
            selection = Rectangle.Empty;
            UpdateTitle();
            canvas.Invalidate();
        }

        private void UpdateTitle()
        {
            Text = "Графический редактор - " + (currentFile == null ? "без имени" : Path.GetFileName(currentFile));
        }

        // Запоминает текущее состояние перед изменением картинки.
        private void SaveUndo()
        {
            undoStack.Push((Bitmap)bmp.Clone());
            redoStack.Clear();
        }

        private Pen CreatePen()
        {
            Pen pen = new Pen(lineColor, int.Parse(cmbWidth.Text));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            return pen;
        }

        // Прямоугольник по двум углам, в каком бы направлении ни тянули мышь.
        private Rectangle MakeRect(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                                 Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private bool IsShapeTool()
        {
            return currentTool == btnLine || currentTool == btnRectangle || currentTool == btnEllipse;
        }

        // Рисует линию/прямоугольник/эллипс. Используется и для «резиновой» фигуры, и для окончательной.
        private void DrawShape(Graphics g, Point a, Point b)
        {
            using (Pen pen = CreatePen())
            {
                if (currentTool == btnLine)
                {
                    g.DrawLine(pen, a, b);
                    return;
                }
                Rectangle r = MakeRect(a, b);
                if ((ModifierKeys & Keys.Shift) != 0)   // с Shift — квадрат / окружность
                    r.Width = r.Height = Math.Min(r.Width, r.Height);
                if (currentTool == btnRectangle)
                    g.DrawRectangle(pen, r);
                else
                    g.DrawEllipse(pen, r);
            }
        }

        // Заливка области одного цвета, начиная с точки p (обход соседей через стек).
        private void FloodFill(Point p, Color color)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int[] pixels = new int[w * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int oldColor = pixels[p.Y * w + p.X];
            int newColor = color.ToArgb();
            if (oldColor != newColor)
            {
                Stack<Point> stack = new Stack<Point>();
                stack.Push(p);
                while (stack.Count > 0)
                {
                    Point q = stack.Pop();
                    if (q.X < 0 || q.Y < 0 || q.X >= w || q.Y >= h) continue;
                    int i = q.Y * w + q.X;
                    if (pixels[i] != oldColor) continue;
                    pixels[i] = newColor;
                    stack.Push(new Point(q.X + 1, q.Y));
                    stack.Push(new Point(q.X - 1, q.Y));
                    stack.Push(new Point(q.X, q.Y + 1));
                    stack.Push(new Point(q.X, q.Y - 1));
                }
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
        }

        private void UpdateColorButtons()
        {
            btnLineColor.Image = ColorSample(lineColor);
            btnFillColor.Image = ColorSample(fillColor);
        }

        // Квадратик 16x16 выбранного цвета для кнопки.
        private Bitmap ColorSample(Color c)
        {
            Bitmap b = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(c);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }
            return b;
        }

        // ---------- Рисование мышью ----------

        private void canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (currentTool == btnFill)
            {
                SaveUndo();
                FloodFill(e.Location, fillColor);
                canvas.Invalidate();
                return;
            }

            if (currentTool == btnPencil) SaveUndo();
            if (currentTool == btnSelect) selection = Rectangle.Empty;
            drawing = true;
            startPoint = lastPoint = endPoint = e.Location;
        }

        private void canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!drawing) return;

            if (currentTool == btnPencil)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen pen = CreatePen())
                    g.DrawLine(pen, lastPoint, e.Location);
                lastPoint = e.Location;
            }
            else if (currentTool == btnSelect)
            {
                // выделение не должно выходить за границы картинки
                selection = Rectangle.Intersect(MakeRect(startPoint, e.Location), new Rectangle(Point.Empty, bmp.Size));
            }
            else
            {
                endPoint = e.Location;   // фигура пока только «резиновая», в bmp не рисуется
            }
            canvas.Invalidate();
        }

        private void canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (!drawing) return;
            drawing = false;

            if (IsShapeTool())
            {
                SaveUndo();
                using (Graphics g = Graphics.FromImage(bmp))
                    DrawShape(g, startPoint, e.Location);
            }
            canvas.Invalidate();
        }

        // Вывод на экран. PictureBox рисует с двойной буферизацией:
        // всё ниже сначала рисуется в буфер в памяти и потом показывается целиком.
        private void canvas_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);

            if (drawing && IsShapeTool())
                DrawShape(e.Graphics, startPoint, endPoint);

            if (selection.Width > 0 && selection.Height > 0)
            {
                using (Pen pen = new Pen(Color.Blue))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, selection);
                }
            }
        }

        // ---------- Панель инструментов ----------

        private void toolButton_Click(object sender, EventArgs e)
        {
            btnPencil.Checked = btnLine.Checked = btnRectangle.Checked =
                btnEllipse.Checked = btnFill.Checked = btnSelect.Checked = false;
            currentTool = (ToolStripButton)sender;
            currentTool.Checked = true;
            selection = Rectangle.Empty;
            canvas.Invalidate();
        }

        private void btnLineColor_Click(object sender, EventArgs e)
        {
            colorDialog1.Color = lineColor;
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                lineColor = colorDialog1.Color;
                UpdateColorButtons();
            }
        }

        private void btnFillColor_Click(object sender, EventArgs e)
        {
            colorDialog1.Color = fillColor;
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                fillColor = colorDialog1.Color;
                UpdateColorButtons();
            }
        }

        // ---------- Меню «Файл» ----------

        private void newMenuItem_Click(object sender, EventArgs e)
        {
            NewImage();
        }

        private void openMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK) return;
            try
            {
                // Копируем в новый Bitmap, чтобы файл не оставался заблокированным.
                Bitmap b;
                using (Image img = Image.FromFile(openFileDialog1.FileName))
                    b = new Bitmap(img);
                undoStack.Clear();
                redoStack.Clear();
                currentFile = openFileDialog1.FileName;
                SetBitmap(b);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл: " + ex.Message, "Ошибка");
            }
        }

        private void saveMenuItem_Click(object sender, EventArgs e)
        {
            if (currentFile == null)
                saveAsMenuItem_Click(sender, e);
            else
                SaveToFile(currentFile);
        }

        private void saveAsMenuItem_Click(object sender, EventArgs e)
        {
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                SaveToFile(saveFileDialog1.FileName);
        }

        // Формат выбирается по расширению файла.
        private void SaveToFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            ImageFormat format = (ext == ".jpg" || ext == ".jpeg") ? ImageFormat.Jpeg : ImageFormat.Bmp;
            bmp.Save(path, format);
            currentFile = path;
            UpdateTitle();
        }

        private void exitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        // ---------- Меню «Правка» ----------

        private void undoMenuItem_Click(object sender, EventArgs e)
        {
            if (undoStack.Count == 0) return;
            redoStack.Push(bmp);
            SetBitmap(undoStack.Pop());
        }

        private void redoMenuItem_Click(object sender, EventArgs e)
        {
            if (redoStack.Count == 0) return;
            undoStack.Push(bmp);
            SetBitmap(redoStack.Pop());
        }

        private void copyMenuItem_Click(object sender, EventArgs e)
        {
            if (selection.Width == 0 || selection.Height == 0) return;
            using (Bitmap part = bmp.Clone(selection, bmp.PixelFormat))
                Clipboard.SetImage(part);
        }

        private void cutMenuItem_Click(object sender, EventArgs e)
        {
            if (selection.Width == 0 || selection.Height == 0) return;
            copyMenuItem_Click(sender, e);
            SaveUndo();
            using (Graphics g = Graphics.FromImage(bmp))
                g.FillRectangle(Brushes.White, selection);
            canvas.Invalidate();
        }

        // Вставка в левый верхний угол выделения (или в угол картинки, если выделения нет).
        private void pasteMenuItem_Click(object sender, EventArgs e)
        {
            if (!Clipboard.ContainsImage()) return;
            SaveUndo();
            using (Image img = Clipboard.GetImage())
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(img, selection.X, selection.Y, img.Width, img.Height);
                selection = Rectangle.Intersect(new Rectangle(selection.Location, img.Size),
                                                new Rectangle(Point.Empty, bmp.Size));
            }
            canvas.Invalidate();
        }
    }
}
