using Bd.Core.Abstractions;
using Bd.Core.Domain;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed partial class BeadEditorViewModel : ObservableObject
{
    private readonly BeadWriteService _writes;
    private readonly ToastService _toast;

    public BeadEditorViewModel(BeadWriteService writes, ToastService toast)
    {
        _writes = writes ?? throw new ArgumentNullException(nameof(writes));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
    }

    public IReadOnlyList<string> Types { get; } = ["task", "bug", "feature", "epic", "chore", "decision", "spike", "story"];

    public IReadOnlyList<string> Priorities { get; } = ["P0", "P1", "P2", "P3", "P4"];

    public event EventHandler<string>? Created;

    public string? CreatedId { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _selectedType = "task";

    [ObservableProperty]
    private string _selectedPriority = "P2";

    [ObservableProperty]
    private string _assigneeInput = string.Empty;

    [ObservableProperty]
    private string _parentId = string.Empty;

    [ObservableProperty]
    private string _labelsText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isBusy;

    public void Reset()
    {
        Title = string.Empty;
        Description = string.Empty;
        SelectedType = "task";
        SelectedPriority = "P2";
        AssigneeInput = string.Empty;
        ParentId = string.Empty;
        LabelsText = string.Empty;
        ErrorText = null;
        CreatedId = null;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorText = "标题不能为空";
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            var labels = LabelsText
                .Split([',', '，', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var request = new CreateBeadRequest
            {
                Title = Title.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
                Type = SelectedType,
                Priority = BdEnumMaps.ToBdPriority(BdEnumMaps.ParsePriority(SelectedPriority)),
                Assignee = string.IsNullOrWhiteSpace(AssigneeInput) ? null : AssigneeInput.Trim(),
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                Labels = labels
            };

            CreatedId = await _writes.CreateAsync(request, CancellationToken.None);
            _toast.Success($"已创建 {CreatedId}");
            Created?.Invoke(this, CreatedId);
        }
        catch (BdException ex)
        {
            ErrorText = ex.UserMessage;
            _toast.Handle(ex);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _toast.Handle(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
