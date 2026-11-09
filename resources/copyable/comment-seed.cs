// Фрагмент усередині DbSeeder.SeedAsync, перед IncidentComments.AddRange.
const string Lab05Marker =
       "<img src=x onerror=\""
       + "document.querySelector('#lab05-marker-target')"
       + ".setAttribute('data-lab05-marker','executed')\">";

// Додайте цей initializer ще одним аргументом AddRange, через кому.
new IncidentComment
   {
       Id = Guid.Parse("30000000-0000-0000-0000-000000000005"),
       Incident = aliceIncident,
       Author = morgan,
       Text = Lab05Marker,
       IsInternal = false,
       CreatedAtUtc = new DateTimeOffset(
           2026, 8, 1, 9, 35, 0, TimeSpan.Zero)
   }
