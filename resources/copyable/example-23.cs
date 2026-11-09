app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("Lab05BrowserProbe");
}
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
// Далі — наявні Map...
