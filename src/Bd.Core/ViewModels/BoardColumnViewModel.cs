using System.Collections.ObjectModel;
using Bd.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bd.Core.ViewModels;

public sealed partial class BoardColumnViewModel : ObservableObject
{
    public BoardColumnViewModel(BoardColumnKind kind, string title, string accent)
    {
        Kind = kind;
        Title = title;
        Accent = accent;
    }

    public BoardColumnKind Kind { get; }

    public string Title { get; }

    public string Accent { get; }

    public ObservableCollection<BeadCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private int _count;

    public bool IsEmpty => Count == 0;

    public void Update(IEnumerable<BeadCardViewModel> cards)
    {
        Cards.Clear();
        foreach (var card in cards)
        {
            Cards.Add(card);
        }

        Count = Cards.Count;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
