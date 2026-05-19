# Markdown Viewer — Testdatei

> Erstellt: Mai 2026
> Rendering Engine: Markdig 1.x auf .NET 10

## Überschriften

# H1 — Überschrift Ebene 1
## H2 — Überschrift Ebene 2
### H3 — Überschrift Ebene 3
#### H4 — Überschrift Ebene 4
##### H5 — Überschrift Ebene 5
###### H6 — Überschrift Ebene 6

## Textformatierung

**Fetter Text** · *Kursiver Text* · ~~Durchgestrichen~~ · `Inline-Code`

**Kombiniert:** *Fetter und ~~durchgestrichener~~ Text*

## Links

- [GitHub](https://github.com)
- [Markdig Repository](https://github.com/xoofx/markdig)
- [Auto-Link](https://example.com)

## Zitate

> Dies ist ein Blockzitat.
>
> Es kann mehrere Absätze enthalten.
>
> > Und sogar verschachtelte Zitate.

## Listen

### Ungeordnete Liste

- Erster Punkt
- Zweiter Punkt
  - Unterpunkt A
  - Unterpunkt B
    - Unter-Unterpunkt
- Dritter Punkt

### Geordnete Liste

1. Schritt 1
2. Schritt 2
3. Schritt 3
   1. Unterschritt 3.1
   2. Unterschritt 3.2

### Aufgabenliste

- [x] Erledigte Aufgabe
- [ ] Noch offene Aufgabe
- [ ] Dringende Aufgabe
- [x] Abgeschlossen

## Code-Blöcke

### C# Beispiel

```csharp
using System;

namespace HelloWorld;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, Markdown Viewer!");
        
        int[] numbers = [1, 2, 3, 4, 5];
        var even = numbers.Where(n => n % 2 == 0);
        
        foreach (var n in even)
        {
            Console.WriteLine($"Gerade Zahl: {n}");
        }
    }
}
```

### JavaScript Beispiel

```javascript
function fibonacci(n) {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

const result = fibonacci(10);
console.log(`Fibonacci(10) = ${result}`);
```

### Python Beispiel

```python
def read_file(path):
    with open(path, 'r', encoding='utf-8') as f:
        return f.read()

content = read_file('beispiel.md')
print(f'Datei hat {len(content)} Zeichen')
```

### Inline Code

Verwende `dotnet build` zum Kompilieren und `dotnet run` zum Ausführen.

## Tabellen

### Einfache Tabelle

| Sprache | Typisierung | Erscheinungsjahr |
|---------|-------------|-----------------:|
| C#      | statisch    | 2000 |
| Python  | dynamisch   | 1991 |
| TypeScript | statisch | 2012 |
| Rust    | statisch    | 2015 |

### Tabelle mit Formatierung

| Feature | Status | Priorität | Bemerkung |
|:--------|:------:|:---------:|:----------|
| **Markdown** | ✅ Fertig | Hoch | GFM-konform |
| *Syntax Highlighting* | ✅ Fertig | Mittel | über CSS |
| ~~HTML Export~~ | ❌ Entfernt | Niedrig | Nicht mehr geplant |
| `Auto-Reload` | ✅ Fertig | Hoch | FileSystemWatcher |

## Bilder

![Placeholder](https://via.placeholder.com/400x200?text=Markdown+Viewer)

## Horizontale Trennlinie

---
 
## Fussnoten

Hier ist ein Text mit einer Fussnote[^1] und noch einer[^2].

[^1]: Die erste Fussnote mit einer Erklärung.
[^2]: Die zweite Fussnote — hier steht mehr Detail.

## Emojis

:smile: :rocket: :fire: :+1: :warning: :book: :computer:

## Definition Lists

Begriff Eins
: Definition für den ersten Begriff

Begriff Zwei
: Definition für den zweiten Begriff
: Weitere Definition

## Tastaturbefehle

Drücke `Strg+S` zum Speichern, `F11` für Vollbild.

## Mathematik (Inline)

Wenn `a^2 + b^2 = c^2`, dann handelt es sich um ein rechtwinkliges Dreieck.

## HTML inline

Details zum <mark>markierten</mark> Text und <kbd>ESC</kbd> zum Schliessen.

---

*Generiert am 17. Mai 2026 · zu Testzwecken*
