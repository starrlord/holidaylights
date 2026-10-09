using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>Where a drag over an edge would insert, relative to a chip.</summary>
public enum InsertionMark
{
    /// <summary>No mark.</summary>
    None,

    /// <summary>Before the chip (left, or above on side edges).</summary>
    Before,

    /// <summary>After the chip.</summary>
    After,
}

/// <summary>
/// One focusable item of the frame editor (PRODUCT-SPEC 3.2.2): a chip (a bulb of an edge, with its order number), an
/// edge's dashed "+", or a corner's bulb. It shows the art on a night well, a remove button on hover or focus, the
/// selection outline, and the drag-and-drop marks (insertion bar, "Replace" outline). Default style in
/// <c>Themes/Generic.xaml</c>.
/// </summary>
public class ArrangementChip : Control
{
    /// <summary>Identifies <see cref="BulbId"/>.</summary>
    public static readonly DependencyProperty BulbIdProperty =
        DependencyProperty.Register(nameof(BulbId), typeof(string), typeof(ArrangementChip), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="Slot"/>.</summary>
    public static readonly DependencyProperty SlotProperty =
        DependencyProperty.Register(nameof(Slot), typeof(CellSlot), typeof(ArrangementChip), new PropertyMetadata(CellSlot.Top));

    /// <summary>Identifies <see cref="Order"/>.</summary>
    public static readonly DependencyProperty OrderProperty =
        DependencyProperty.Register(nameof(Order), typeof(int), typeof(ArrangementChip), new PropertyMetadata(0));

    /// <summary>Identifies <see cref="IsPlus"/>.</summary>
    public static readonly DependencyProperty IsPlusProperty =
        DependencyProperty.Register(nameof(IsPlus), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsSelected"/>.</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsAnimated"/>.</summary>
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.Register(nameof(IsAnimated), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="Insertion"/>.</summary>
    public static readonly DependencyProperty InsertionProperty =
        DependencyProperty.Register(nameof(Insertion), typeof(InsertionMark), typeof(ArrangementChip), new PropertyMetadata(InsertionMark.None));

    /// <summary>Identifies <see cref="IsReplaceTarget"/>.</summary>
    public static readonly DependencyProperty IsReplaceTargetProperty =
        DependencyProperty.Register(nameof(IsReplaceTarget), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsVertical"/>.</summary>
    public static readonly DependencyProperty IsVerticalProperty =
        DependencyProperty.Register(nameof(IsVertical), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="Services"/>.</summary>
    public static readonly DependencyProperty ServicesProperty =
        DependencyProperty.Register(nameof(Services), typeof(IAppServices), typeof(ArrangementChip), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="TryOnBulbId"/>.</summary>
    public static readonly DependencyProperty TryOnBulbIdProperty =
        DependencyProperty.Register(nameof(TryOnBulbId), typeof(string), typeof(ArrangementChip),
            new PropertyMetadata(null, (d, e) => d.SetValue(IsTryingOnPropertyKey, e.NewValue is not null)));

    private static readonly DependencyPropertyKey IsTryingOnPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsTryingOn), typeof(bool), typeof(ArrangementChip), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsTryingOn"/>.</summary>
    public static readonly DependencyProperty IsTryingOnProperty = IsTryingOnPropertyKey.DependencyProperty;

    /// <summary>Identifies the <see cref="RemoveRequested"/> routed event.</summary>
    public static readonly RoutedEvent RemoveRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(RemoveRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ArrangementChip));

    /// <summary>The command of the remove button.</summary>
    public static readonly RoutedCommand RemoveCommand = new(nameof(RemoveCommand), typeof(ArrangementChip));

    static ArrangementChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ArrangementChip), new FrameworkPropertyMetadata(typeof(ArrangementChip)));
        FocusableProperty.OverrideMetadata(typeof(ArrangementChip), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates the chip.</summary>
    public ArrangementChip() => CommandBindings.Add(new CommandBinding(RemoveCommand, (_, e) =>
    {
        RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent, this));
        e.Handled = true;
    }));

    /// <summary>Raised when the chip's remove button was pressed.</summary>
    public event RoutedEventHandler RemoveRequested
    {
        add => AddHandler(RemoveRequestedEvent, value);
        remove => RemoveHandler(RemoveRequestedEvent, value);
    }

    /// <summary>The bulb shown, or null for an empty corner or a "+".</summary>
    public string? BulbId
    {
        get => (string?)GetValue(BulbIdProperty);
        set => SetValue(BulbIdProperty, value);
    }

    /// <summary>The art slot (the edge or the corner).</summary>
    public CellSlot Slot
    {
        get => (CellSlot)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <summary>The 1-based order number on an edge, or 0 for none.</summary>
    public int Order
    {
        get => (int)GetValue(OrderProperty);
        set => SetValue(OrderProperty, value);
    }

    /// <summary>True for the dashed "+" placeholder.</summary>
    public bool IsPlus
    {
        get => (bool)GetValue(IsPlusProperty);
        set => SetValue(IsPlusProperty, value);
    }

    /// <summary>True when the item is the target (2 DIP accent outline).</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Animate the art with the lights (corners); chips show the lit frame.</summary>
    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>The insertion bar of a drag over the edge.</summary>
    public InsertionMark Insertion
    {
        get => (InsertionMark)GetValue(InsertionProperty);
        set => SetValue(InsertionProperty, value);
    }

    /// <summary>True when a drop would replace this chip (a full edge).</summary>
    public bool IsReplaceTarget
    {
        get => (bool)GetValue(IsReplaceTargetProperty);
        set => SetValue(IsReplaceTargetProperty, value);
    }

    /// <summary>True on the left and right edges (the insertion bar is horizontal).</summary>
    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    /// <summary>The services for the art.</summary>
    public IAppServices? Services
    {
        get => (IAppServices?)GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    /// <summary>A bulb being tried on in this place (shown instead of <see cref="BulbId"/>, outlined as a try-on), or null.</summary>
    public string? TryOnBulbId
    {
        get => (string?)GetValue(TryOnBulbIdProperty);
        set => SetValue(TryOnBulbIdProperty, value);
    }

    /// <summary>True while <see cref="TryOnBulbId"/> is shown.</summary>
    public bool IsTryingOn => (bool)GetValue(IsTryingOnProperty);

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ArrangementChipAutomationPeer(this);

    /// <summary>Screen readers see a selectable list item named by the editor ("Standard Bulbs, position 1 of 2 on the top edge.").</summary>
    private sealed class ArrangementChipAutomationPeer(ArrangementChip owner) : FrameworkElementAutomationPeer(owner), ISelectionItemProvider
    {
        private ArrangementChip Chip => (ArrangementChip)Owner;

        public bool IsSelected => Chip.IsSelected;

        public IRawElementProviderSimple? SelectionContainer => null;

        public void AddToSelection() => Chip.Focus();

        public void RemoveFromSelection()
        {
        }

        public void Select() => Chip.Focus();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => nameof(ArrangementChip);

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
