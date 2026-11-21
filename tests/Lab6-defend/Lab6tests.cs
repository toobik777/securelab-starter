using System;
using System.Collections.Generic;

record Case(string Name, string Title, int Priority, int Status, string[] Errors);

class Lab6Demo
{
    static void Main()
    {
        var cases = new[]
        {
            new Case("1. Контрприклад J", "Val", 5, 400, new[] { "priority" }),
            new Case("2. Свідок K",      "   ", 2, 400, new[] { "title" }),
            new Case("3. Обидва невірні", "   ", 9, 400, new[] { "title", "priority" }),
            new Case("4. Обидва вірні",   "Val", 2, 200, Array.Empty<string>())
        };

        Console.WriteLine("Кейс                | До (стара) | Після (нова) | Diff");
        Console.WriteLine(new string('-', 55));

        foreach (var c in cases)
        {
            var errs = new HashSet<string>(c.Errors);
            bool oldV = c.Status == 400 && errs.Count > 0;
            bool newV = c.Status == 400 && errs.Contains("title");

            string diff = oldV != newV ? "⚠️ ВИПРАВЛЕНО" : "Без змін";
            string d1 = oldV ? "Прийнято" : "Відхилено";
            string d2 = newV ? "Прийнято" : "Відхилено";

            Console.WriteLine($"{c.Name,-18} | {d1,-10} | {d2,-12} | {diff}");
        }
    }
}