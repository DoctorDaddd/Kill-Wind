using System;
using System.Drawing;
using System.Windows.Forms;

public sealed class GameState
{
    public volatile int Hp = 100;
    public volatile int Mp = 50;
    public volatile int Gold = 12345;
    public float Speed = 1.0f;
    public double CriticalMultiplier = 2.0;
}

public sealed class PointerLevel
{
    public PointerLevel Next;
    public GameState State;
}

public sealed class MemoryTestGame : Form
{
    // Static fields provide stable values for the Int32/Float/Double scan smoke tests.
    public static volatile int StaticHealth = 100;
    public static long StaticExperience = 9000000000L;
    public static float StaticSpeed = 1.0f;
    public static double StaticAccuracy = 0.875;

    // This heap graph intentionally gives the pointer scanner a small multi-level target.
    public static PointerLevel Root;
    private static readonly GameState DynamicState;
    private readonly Label _values;

    static MemoryTestGame()
    {
        DynamicState = new GameState();
        Root = new PointerLevel { State = null };
        Root.Next = new PointerLevel { State = null };
        Root.Next.Next = new PointerLevel { State = DynamicState };
    }

    public MemoryTestGame()
    {
        Text = "MemoryTestGame - Universal Game Editor";
        Width = 620;
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 32, 40);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var title = new Label { Text = "MemoryTestGame", AutoSize = true, Location = new Point(24, 20), Font = new Font(Font, FontStyle.Bold) };
        _values = new Label { AutoSize = true, Location = new Point(24, 62), ForeColor = Color.LightSteelBlue };
        Controls.Add(title);
        Controls.Add(_values);

        AddButton("Take Damage", 24, 170, () => { DynamicState.Hp -= 10; StaticHealth = DynamicState.Hp; RefreshValues(); });
        AddButton("Heal", 154, 170, () => { DynamicState.Hp += 10; StaticHealth = DynamicState.Hp; RefreshValues(); });
        AddButton("Spend Gold", 284, 170, () => { DynamicState.Gold -= 100; RefreshValues(); });
        AddButton("Gain Gold", 414, 170, () => { DynamicState.Gold += 100; RefreshValues(); });
        AddButton("Speed +", 24, 220, () => { DynamicState.Speed += 0.25f; StaticSpeed = DynamicState.Speed; RefreshValues(); });
        AddButton("Speed -", 154, 220, () => { DynamicState.Speed -= 0.25f; StaticSpeed = DynamicState.Speed; RefreshValues(); });
        RefreshValues();
    }

    private void AddButton(string text, int x, int y, Action action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Width = 112, Height = 32 };
        button.Click += (_sender, _args) => action();
        Controls.Add(button);
    }

    private void RefreshValues()
    {
        _values.Text = String.Format("HP: {0}\r\nMP: {1}\r\nGold: {2}\r\nSpeed: {3:F2}\r\nCriticalMultiplier: {4:F2}\r\nStaticExperience: {5}",
            DynamicState.Hp, DynamicState.Mp, DynamicState.Gold, DynamicState.Speed, DynamicState.CriticalMultiplier, StaticExperience);
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MemoryTestGame());
    }
}
