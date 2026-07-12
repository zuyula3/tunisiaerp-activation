using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// ═══════════════════════════════════════════════════════════════════════════
// SERVEUR D'ACTIVATION TUNISIA ERP — avec Turso (SQLite cloud persistant)
// ═══════════════════════════════════════════════════════════════════════════

const string CLE_SECRETE       = "TunisiaERP-2026-ClePriveeVendeur-Yessinus@1042015";
const string MOT_DE_PASSE_ADMIN = "Jedjud@2672017";

// ── Turso config (variables d'environnement Render) ───────────────────────
string TursoUrl()   => Environment.GetEnvironmentVariable("TURSO_URL")   ?? "";
string TursoToken() => Environment.GetEnvironmentVariable("TURSO_TOKEN") ?? "";

const string PageAccueilHtml = """
<!DOCTYPE html><html lang="fr"><head><meta charset="UTF-8"><title>Tunisia ERP Server</title>
<style>body{font-family:Segoe UI,sans-serif;background:#0f172a;margin:0;display:flex;align-items:center;justify-content:center;height:100vh;color:white}.card{background:#1e293b;border-radius:16px;padding:48px;text-align:center;max-width:480px}h1{color:#3b82f6;font-size:2rem;margin-bottom:8px}p{color:#94a3b8;margin-bottom:32px}.btn{display:inline-block;padding:14px 28px;border-radius:8px;text-decoration:none;font-weight:600;margin:8px;font-size:15px}.btn-blue{background:#3b82f6;color:white}.btn-red{background:#dc2626;color:white}.status{background:#022c22;border:1px solid #16a34a;color:#4ade80;padding:10px 20px;border-radius:8px;font-size:13px;margin-bottom:24px}</style>
</head><body><div class="card"><h1>🇹🇳 Tunisia ERP</h1><p>Serveur d'activation et de backup</p><div class="status">✅ Opérationnel — Turso Cloud</div><a href="/admin" class="btn btn-red">🔑 Admin Licences</a><a href="/backup" class="btn btn-blue">💾 Gestion Backup</a></div></body></html>
""";

const string PageAdminHtml = """
<!DOCTYPE html><html lang="fr"><head><meta charset="UTF-8"><title>Admin Licences - Tunisia ERP</title>
<style>body{font-family:Segoe UI,sans-serif;background:#f8fafc;margin:0;padding:24px;color:#0f172a}h1{color:#dc2626}.card{background:white;border-radius:8px;padding:20px;margin-bottom:20px;box-shadow:0 1px 3px rgba(0,0,0,.1)}input,select{padding:8px;margin:4px 0;width:100%;box-sizing:border-box;border:1px solid #e2e8f0;border-radius:4px}button{background:#dc2626;color:white;border:none;padding:10px 20px;border-radius:4px;cursor:pointer;font-weight:600}button:hover{background:#b91c1c}table{width:100%;border-collapse:collapse;margin-top:12px}th,td{text-align:left;padding:8px;border-bottom:1px solid #e2e8f0;font-size:13px}th{background:#f1f5f9}.badge{padding:3px 8px;border-radius:4px;font-size:11px;font-weight:600}.badge-ok{background:#dcfce7;color:#16a34a}.badge-revoke{background:#fee2e2;color:#dc2626}.btn-revoke{background:#f59e0b;padding:4px 10px;font-size:12px}#result{margin-top:10px;padding:10px;border-radius:4px;display:none}</style>
</head><body>
<h1>🔑 Admin Licences - Tunisia ERP</h1>
<div class="card"><h3>Mot de passe admin</h3><input type="password" id="mdp" placeholder="Mot de passe admin"></div>
<div class="card"><h3>Creer une nouvelle licence</h3><input id="idMachine" placeholder="ID Machine (XXXX-XXXX-XXXX-XXXX)"><input id="entreprise" placeholder="Nom de l'entreprise"><select id="edition"><option value="Standard">Standard</option><option value="Pro">Pro</option><option value="Entreprise">Entreprise</option></select><input id="expiration" type="date"><button onclick="creerLicence()">Generer la cle</button><div id="result"></div></div>
<div class="card"><h3>Licences existantes</h3><button onclick="chargerListe()">Actualiser</button><table id="tableLicences"><thead><tr><th>ID Machine</th><th>Entreprise</th><th>Edition</th><th>Cle</th><th>Expiration</th><th>Statut</th><th></th></tr></thead><tbody></tbody></table></div>
<script>
function mdp(){return document.getElementById('mdp').value;}
async function creerLicence(){const body={idMachine:document.getElementById('idMachine').value.trim(),nomEntreprise:document.getElementById('entreprise').value.trim(),edition:document.getElementById('edition').value,dateExpiration:document.getElementById('expiration').value||null};const r=await fetch('/admin/creer',{method:'POST',headers:{'Content-Type':'application/json','X-Admin-Password':mdp()},body:JSON.stringify(body)});const data=await r.json();const div=document.getElementById('result');div.style.display='block';if(r.ok){div.style.background='#dcfce7';div.style.color='#16a34a';div.innerHTML='<b>Cle generee :</b> <code style="font-size:16px">'+data.cle+'</code><br>Envoyez cette cle au client.';chargerListe();}else{div.style.background='#fee2e2';div.style.color='#dc2626';div.innerText=data.erreur||'Erreur';}}
async function chargerListe(){const r=await fetch('/admin/liste',{headers:{'X-Admin-Password':mdp()}});if(!r.ok){alert('Mot de passe incorrect');return;}const data=await r.json();const tbody=document.querySelector('#tableLicences tbody');tbody.innerHTML='';for(const l of data){const tr=document.createElement('tr');tr.innerHTML=`<td>${l.idMachine}</td><td>${l.entreprise||''}</td><td>${l.edition}</td><td><code>${l.cle}</code></td><td>${l.dateExpiration||'Illimitee'}</td><td>${l.revoquee?'<span class="badge badge-revoke">Revoquee</span>':'<span class="badge badge-ok">Active</span>'}</td><td>${l.revoquee?'':'<button class="btn-revoke" onclick="revoquer('+l.id+')">Revoquer</button>'}</td>`;tbody.appendChild(tr);}}
async function revoquer(id){if(!confirm('Revoquer cette licence ?'))return;await fetch('/admin/revoquer/'+id,{method:'POST',headers:{'X-Admin-Password':mdp()}});chargerListe();}
</script></body></html>
""";

const string PageBackupHtml = """
<!DOCTYPE html><html lang="fr"><head><meta charset="UTF-8"><title>Backup - Tunisia ERP</title>
<style>*{box-sizing:border-box;margin:0;padding:0}body{font-family:Segoe UI,sans-serif;background:#0f172a;color:#e2e8f0;min-height:100vh;padding:32px 16px}.container{max-width:600px;margin:0 auto}h1{color:#3b82f6;font-size:1.8rem;margin-bottom:6px}.sub{color:#64748b;margin-bottom:32px;font-size:14px}.card{background:#1e293b;border-radius:12px;padding:24px;margin-bottom:20px;border:1px solid #334155}.card h3{font-size:12px;margin-bottom:16px;color:#94a3b8;text-transform:uppercase;letter-spacing:.05em}input[type=password]{width:100%;padding:10px 14px;background:#0f172a;border:1px solid #334155;border-radius:8px;color:white;font-size:15px;margin-bottom:12px}.btn{display:inline-flex;align-items:center;gap:8px;padding:12px 24px;border-radius:8px;border:none;cursor:pointer;font-size:14px;font-weight:600;width:100%;justify-content:center;margin-bottom:10px}.btn-blue{background:#3b82f6;color:white}.btn-green{background:#16a34a;color:white}.info-box{background:#0f172a;border:1px solid #334155;border-radius:8px;padding:16px;font-size:13px;color:#94a3b8;min-height:60px}.info-box.ok{border-color:#16a34a;color:#4ade80}.info-box.err{border-color:#dc2626;color:#f87171}</style>
</head><body><div class="container">
<h1>💾 Gestion Backup</h1><p class="sub">Tunisia ERP — Sauvegarde persistante sur Turso Cloud</p>
<div class="card"><h3>🔐 Authentification</h3><input type="password" id="mdp" placeholder="Mot de passe admin"></div>
<div class="card"><h3>📊 Dernier backup</h3><div class="info-box" id="infoBox">Cliquez sur "Actualiser"...</div><br><button class="btn btn-blue" onclick="actualiser()">🔄 Actualiser</button></div>
<div class="card"><h3>⚡ Actions</h3><button class="btn btn-green" onclick="telecharger()">⬇ Télécharger le backup (.sqlite)</button><div id="statusBox" class="info-box" style="display:none"></div></div>
</div>
<script>
function mdp(){return document.getElementById('mdp').value;}
function showStatus(msg,ok=true){const b=document.getElementById('statusBox');b.style.display='block';b.className='info-box '+(ok?'ok':'err');b.innerHTML=msg;}
async function actualiser(){if(!mdp()){alert('Entrez le mot de passe');return;}try{const r=await fetch('/backup/info',{headers:{'X-Admin-Password':mdp()}});const d=await r.json();const box=document.getElementById('infoBox');if(!r.ok){box.className='info-box err';box.innerHTML='❌ '+d.erreur;return;}if(!d.disponible){box.className='info-box';box.innerHTML='⚠️ Aucun backup disponible.';return;}box.className='info-box ok';box.innerHTML=`✅ <b>Disponible</b><br>📅 Date: <b>${d.dateModification}</b><br>💿 Taille: <b>${d.tailleMo} Mo</b><br>📦 Sauvegardes: <b>${d.nbBackupsStockes}</b>`;}catch(e){document.getElementById('infoBox').className='info-box err';document.getElementById('infoBox').innerHTML='❌ '+e.message;}}
async function telecharger(){if(!mdp()){alert('Entrez le mot de passe');return;}showStatus('⏳ Téléchargement...');try{const r=await fetch('/backup/download',{headers:{'X-Admin-Password':mdp()}});if(!r.ok){const d=await r.json();showStatus('❌ '+d.erreur,false);return;}const blob=await r.blob();const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='tndb_backup_'+new Date().toISOString().slice(0,10)+'.sqlite';document.body.appendChild(a);a.click();document.body.removeChild(a);URL.revokeObjectURL(url);showStatus('✅ Backup téléchargé!');}catch(e){showStatus('❌ '+e.message,false);}}
</script></body></html>
""";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

// ── Turso HTTP API helper ─────────────────────────────────────────────────
async Task<List<Dictionary<string,string>>> TursoQuery(
    IHttpClientFactory factory, string sql, List<object?>? args = null)
{
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TursoToken());

    var tursoArgs = (args ?? new List<object?>()).Select(a =>
        a == null
            ? (object)new { type = "null" }
            : new { type = "text", value = a.ToString() ?? "" }
    ).ToArray();

    var payload = new {
        requests = new object[] {
            new { type = "execute", stmt = new { sql, args = tursoArgs } },
            new { type = "close" }
        }
    };

    var json    = JsonSerializer.Serialize(payload);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var url     = TursoUrl().Replace("libsql://", "https://") + "/v2/pipeline";
    var resp    = await client.PostAsync(url, content);
    var body    = await resp.Content.ReadAsStringAsync();

    var rows = new List<Dictionary<string,string>>();
    try {
        var doc    = JsonDocument.Parse(body).RootElement;
        // Turso restituisce: {"results":[{"type":"ok","response":{"type":"execute","result":{...}}},...]}
        var results = doc.GetProperty("results");
        var result  = results[0].GetProperty("response").GetProperty("result");
        var cols    = result.GetProperty("cols").EnumerateArray()
                           .Select(c => c.GetProperty("name").GetString()!).ToList();
        foreach (var row in result.GetProperty("rows").EnumerateArray())
        {
            var dict = new Dictionary<string,string>();
            var vals = row.EnumerateArray().ToList();
            for (int i = 0; i < cols.Count; i++)
            {
                var cell     = vals[i];
                string type  = cell.GetProperty("type").GetString() ?? "null";
                string value = type == "null" ? "" :
                               cell.TryGetProperty("value", out var v) ? v.GetString() ?? v.ToString() : "";
                dict[cols[i]] = value;
            }
            rows.Add(dict);
        }
    } catch { }
    return rows;
}

// ── Init DB Turso ─────────────────────────────────────────────────────────
var httpFactory = app.Services.GetRequiredService<IHttpClientFactory>();
await TursoQuery(httpFactory, @"CREATE TABLE IF NOT EXISTS Licences(
    IdLicence INTEGER PRIMARY KEY AUTOINCREMENT,
    IdMachine TEXT NOT NULL, Cle TEXT NOT NULL UNIQUE,
    NomEntreprise TEXT DEFAULT '', Edition TEXT DEFAULT 'Standard',
    DateCreation TEXT NOT NULL, DateExpiration TEXT,
    DateActivation TEXT, Revoquee INTEGER DEFAULT 0,
    UNIQUE(IdMachine, Edition))");
await TursoQuery(httpFactory, @"CREATE TABLE IF NOT EXISTS Backups(
    IdBackup INTEGER PRIMARY KEY AUTOINCREMENT,
    NomFichier TEXT NOT NULL, DateUpload TEXT NOT NULL,
    TailleOctets INTEGER DEFAULT 0, Donnees TEXT)");

string CalculerCle(string idMachine, string edition)
{
    string donnees = idMachine.ToUpper()+"|"+edition.ToUpper()+"|"+CLE_SECRETE;
    using var sha  = SHA256.Create();
    var hash       = sha.ComputeHash(Encoding.UTF8.GetBytes(donnees));
    var hex        = Convert.ToHexString(hash)[..20];
    return string.Join("-", new[]{hex[..5],hex[5..10],hex[10..15],hex[15..20]});
}

bool VerifierAdmin(HttpRequest req) =>
    req.Headers["X-Admin-Password"].ToString() == MOT_DE_PASSE_ADMIN;

// ── API CLIENT: activation ────────────────────────────────────────────────
app.MapPost("/api/activer", async (ActivationRequest req, IHttpClientFactory factory) =>
{
    var rows = await TursoQuery(factory,
        "SELECT * FROM Licences WHERE IdMachine=? AND Edition=?",
        new List<object?> { req.IdMachine.ToUpper(), req.Edition });

    if (rows.Count == 0)
        return Results.Json(new ActivationResponse(false, "Aucune licence trouvee. Contactez le vendeur.", null, null));

    var row = rows[0];
    if (row["Revoquee"] == "1")
        return Results.Json(new ActivationResponse(false, "Licence revoquee. Contactez le vendeur.", null, null));
    if (!string.Equals(row["Cle"], req.Cle.Trim(), StringComparison.OrdinalIgnoreCase))
        return Results.Json(new ActivationResponse(false, "Cle de licence incorrecte.", null, null));

    string? dateExpStr = row["DateExpiration"];
    DateTime? dateExp  = string.IsNullOrEmpty(dateExpStr) ? null : DateTime.Parse(dateExpStr);
    if (dateExp.HasValue && dateExp.Value.Date < DateTime.UtcNow.Date)
        return Results.Json(new ActivationResponse(false, "Licence expirée le "+dateExp.Value.ToString("dd/MM/yyyy")+".", null, null));

    await TursoQuery(factory,
        "UPDATE Licences SET DateActivation=COALESCE(DateActivation,?) WHERE IdMachine=? AND Edition=?",
        new List<object?> { DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), req.IdMachine.ToUpper(), req.Edition });

    return Results.Json(new ActivationResponse(true, "Activation reussie.", dateExp?.ToString("yyyy-MM-dd"), row["NomEntreprise"]));
});

// ── ADMIN: creer licence ──────────────────────────────────────────────────
app.MapPost("/admin/creer", async (HttpRequest http, CreerLicenceRequest req, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    string cle      = CalculerCle(req.IdMachine, req.Edition);
    string idMach   = req.IdMachine.ToUpper();
    string dateNow  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    string dateExp  = req.DateExpiration ?? "";

    // Verifica se esiste già
    var existing = await TursoQuery(factory,
        "SELECT IdLicence FROM Licences WHERE IdMachine=? AND Edition=?",
        new List<object?> { idMach, req.Edition });

    if (existing.Count > 0)
    {
        // Aggiorna
        await TursoQuery(factory,
            "UPDATE Licences SET Cle=?, NomEntreprise=?, DateExpiration=?, Revoquee=0 WHERE IdMachine=? AND Edition=?",
            new List<object?> { cle, req.NomEntreprise, dateExp, idMach, req.Edition });
    }
    else
    {
        // Inserisce
        await TursoQuery(factory,
            "INSERT INTO Licences(IdMachine,Cle,NomEntreprise,Edition,DateCreation,DateExpiration) VALUES(?,?,?,?,?,?)",
            new List<object?> { idMach, cle, req.NomEntreprise, req.Edition, dateNow, dateExp });
    }

    return Results.Json(new { cle, message = "Licence creee. Envoyez cette cle au client." });
});

// ── ADMIN: lister licences ────────────────────────────────────────────────
app.MapGet("/admin/liste", async (HttpRequest http, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    var rows = await TursoQuery(factory, "SELECT * FROM Licences ORDER BY DateCreation DESC");
    var liste = rows.Select((r,i) => new {
        id             = i+1,
        idMachine      = r.GetValueOrDefault("IdMachine",""),
        cle            = r.GetValueOrDefault("Cle",""),
        entreprise     = r.GetValueOrDefault("NomEntreprise",""),
        edition        = r.GetValueOrDefault("Edition",""),
        dateCreation   = r.GetValueOrDefault("DateCreation",""),
        dateExpiration = r.GetValueOrDefault("DateExpiration",""),
        dateActivation = r.GetValueOrDefault("DateActivation",""),
        revoquee       = r.GetValueOrDefault("Revoquee","0") == "1"
    }).ToList();
    return Results.Json(liste);
});

// ── ADMIN: revoquer ───────────────────────────────────────────────────────
app.MapPost("/admin/revoquer/{id}", async (HttpRequest http, int id, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    await TursoQuery(factory, "UPDATE Licences SET Revoquee=1 WHERE IdLicence=?", new List<object?>{id});
    return Results.Json(new { message = "Licence revoquee." });
});

// ── BACKUP: upload ────────────────────────────────────────────────────────
app.MapPost("/backup/upload", async (HttpRequest http, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    try {
        using var ms = new MemoryStream();
        await http.Body.CopyToAsync(ms);
        byte[] data = ms.ToArray();
        if (data.Length == 0) return Results.Json(new { erreur = "Fichier vide" }, statusCode: 400);

        string timestamp  = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        string nomFichier = $"tndb_{timestamp}.sqlite";
        string b64        = Convert.ToBase64String(data);

        await TursoQuery(factory,
            "INSERT INTO Backups(NomFichier,DateUpload,TailleOctets,Donnees) VALUES(?,?,?,?)",
            new List<object?> { nomFichier, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), data.Length, b64 });

        // Mantieni solo ultimi 7
        await TursoQuery(factory,
            "DELETE FROM Backups WHERE IdBackup NOT IN (SELECT IdBackup FROM Backups ORDER BY IdBackup DESC LIMIT 7)");

        return Results.Json(new {
            message = "Backup reçu et sauvegardé avec succès.",
            fichier = nomFichier, taille = data.Length,
            date = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss") + " UTC" });
    } catch (Exception ex) {
        return Results.Json(new { erreur = "Erreur: "+ex.Message }, statusCode: 500);
    }
});

// ── BACKUP: download ──────────────────────────────────────────────────────
app.MapGet("/backup/download", async (HttpRequest http, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    var rows = await TursoQuery(factory, "SELECT Donnees,NomFichier FROM Backups ORDER BY IdBackup DESC LIMIT 1");
    if (rows.Count == 0) return Results.Json(new { erreur = "Aucun backup disponible." }, statusCode: 404);
    var bytes = Convert.FromBase64String(rows[0].GetValueOrDefault("Donnees",""));
    return Results.File(bytes, "application/octet-stream", rows[0].GetValueOrDefault("NomFichier","tndb.sqlite"));
});

// ── BACKUP: info ──────────────────────────────────────────────────────────
app.MapGet("/backup/info", async (HttpRequest http, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);
    var rows = await TursoQuery(factory, "SELECT NomFichier,DateUpload,TailleOctets FROM Backups ORDER BY IdBackup DESC LIMIT 1");
    if (rows.Count == 0) return Results.Json(new { disponible = false, message = "Aucun backup sur le serveur." });
    var nb    = await TursoQuery(factory, "SELECT COUNT(*) as nb FROM Backups");
    int count = int.TryParse(nb.FirstOrDefault()?.GetValueOrDefault("nb","0"), out var n) ? n : 0;
    var row   = rows[0];
    long taille = long.TryParse(row.GetValueOrDefault("TailleOctets","0"), out var t) ? t : 0;
    return Results.Json(new {
        disponible       = true,
        dateModification = row.GetValueOrDefault("DateUpload","") + " UTC",
        tailleMo         = Math.Round(taille/1024.0/1024.0, 2),
        tailleOctets     = taille,
        nbBackupsStockes = count });
});

// ── DEBUG: test connessione Turso (raw response) ─────────────────────────
app.MapGet("/debug/turso", async (HttpRequest http, IHttpClientFactory factory) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Non autorise" }, statusCode: 401);
    try {
        // Restituisce la risposta RAW di Turso per capire il formato esatto
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TursoToken());
        var payload = new {
            requests = new object[] {
                new { type = "execute", stmt = new { sql = "SELECT COUNT(*) as nb FROM Licences", args = Array.Empty<object>() } },
                new { type = "close" }
            }
        };
        var json    = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url     = TursoUrl().Replace("libsql://", "https://") + "/v2/pipeline";
        var resp    = await client.PostAsync(url, content);
        var body    = await resp.Content.ReadAsStringAsync();
        // Restituisce la risposta grezza di Turso
        return Results.Content(body, "application/json");
    } catch (Exception ex) {
        return Results.Json(new { ok = false, erreur = ex.Message });
    }
});

// ── PAGES WEB ─────────────────────────────────────────────────────────────
app.MapGet("/admin",  () => Results.Content(PageAdminHtml,   "text/html; charset=utf-8"));
app.MapGet("/backup", () => Results.Content(PageBackupHtml,  "text/html; charset=utf-8"));
app.MapGet("/",       () => Results.Content(PageAccueilHtml, "text/html; charset=utf-8"));

app.Run();

// ── Modeles ───────────────────────────────────────────────────────────────
record ActivationRequest(string IdMachine, string Cle, string Edition);
record ActivationResponse(bool Succes, string Message, string? DateExpiration, string? NomEntreprise);
record CreerLicenceRequest(string IdMachine, string NomEntreprise, string Edition, string? DateExpiration);
