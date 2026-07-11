using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// ═══════════════════════════════════════════════════════════════════════════
// SERVEUR D'ACTIVATION TUNISIA ERP
// ═══════════════════════════════════════════════════════════════════════════
// Petit service web qui :
//  - Genere des cles de licence liees a un ID Machine (cote vendeur, via /admin)
//  - Verifie/active une licence quand un client le demande (cote client, via /api/activer)
//  - Permet de revoquer une licence a tout moment (cote vendeur, via /admin)
//
// Le client n'a besoin d'internet QU'AU MOMENT DE L'ACTIVATION. Une fois activee,
// Tunisia ERP fonctionne hors-ligne indefiniment (jusqu'a expiration de la licence).
// ═══════════════════════════════════════════════════════════════════════════

// ⚠️ CHANGEZ CES DEUX VALEURS avant de deployer en production
const string CLE_SECRETE = "TunisiaERP-2026-ClePriveeVendeur-Yessinus@1042015";
const string MOT_DE_PASSE_ADMIN = "Jedjud@2672017";

// ── Page admin HTML minimaliste (pas besoin de frontend separe) ───────────
const string PageAdminHtml = """
<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="UTF-8">
<title>Admin Licences - Tunisia ERP</title>
<style>
  body{font-family:Segoe UI,Arial,sans-serif;background:#f8fafc;margin:0;padding:24px;color:#0f172a}
  h1{color:#dc2626}
  .card{background:white;border-radius:8px;padding:20px;margin-bottom:20px;box-shadow:0 1px 3px rgba(0,0,0,.1)}
  input,select{padding:8px;margin:4px 0;width:100%;box-sizing:border-box;border:1px solid #e2e8f0;border-radius:4px}
  button{background:#dc2626;color:white;border:none;padding:10px 20px;border-radius:4px;cursor:pointer;font-weight:600}
  button:hover{background:#b91c1c}
  table{width:100%;border-collapse:collapse;margin-top:12px}
  th,td{text-align:left;padding:8px;border-bottom:1px solid #e2e8f0;font-size:13px}
  th{background:#f1f5f9}
  .badge{padding:3px 8px;border-radius:4px;font-size:11px;font-weight:600}
  .badge-ok{background:#dcfce7;color:#16a34a}
  .badge-revoke{background:#fee2e2;color:#dc2626}
  .btn-revoke{background:#f59e0b;padding:4px 10px;font-size:12px}
  #result{margin-top:10px;padding:10px;border-radius:4px;display:none}
</style>
</head>
<body>
<h1>🔑 Admin Licences - Tunisia ERP</h1>

<div class="card">
  <h3>Mot de passe admin</h3>
  <input type="password" id="mdp" placeholder="Mot de passe admin">
</div>

<div class="card">
  <h3>Creer une nouvelle licence</h3>
  <input id="idMachine" placeholder="ID Machine du client (XXXX-XXXX-XXXX-XXXX)">
  <input id="entreprise" placeholder="Nom de l'entreprise">
  <select id="edition">
    <option value="Standard">Standard</option>
    <option value="Pro">Pro</option>
    <option value="Entreprise">Entreprise</option>
  </select>
  <input id="expiration" type="date">
  <button onclick="creerLicence()">Generer la cle</button>
  <div id="result"></div>
</div>

<div class="card">
  <h3>Licences existantes</h3>
  <button onclick="chargerListe()">Actualiser</button>
  <table id="tableLicences">
    <thead><tr><th>ID Machine</th><th>Entreprise</th><th>Edition</th><th>Cle</th><th>Expiration</th><th>Statut</th><th></th></tr></thead>
    <tbody></tbody>
  </table>
</div>

<script>
function mdp(){ return document.getElementById('mdp').value; }

async function creerLicence(){
  const body = {
    idMachine: document.getElementById('idMachine').value.trim(),
    nomEntreprise: document.getElementById('entreprise').value.trim(),
    edition: document.getElementById('edition').value,
    dateExpiration: document.getElementById('expiration').value || null
  };
  const r = await fetch('/admin/creer', {
    method:'POST', headers:{'Content-Type':'application/json','X-Admin-Password':mdp()},
    body: JSON.stringify(body)
  });
  const data = await r.json();
  const div = document.getElementById('result');
  div.style.display='block';
  if(r.ok){
    div.style.background='#dcfce7'; div.style.color='#16a34a';
    div.innerHTML = '<b>Cle generee :</b> <code style="font-size:16px">'+data.cle+'</code><br>Envoyez cette cle au client.';
    chargerListe();
  } else {
    div.style.background='#fee2e2'; div.style.color='#dc2626';
    div.innerText = data.erreur || 'Erreur';
  }
}

async function chargerListe(){
  const r = await fetch('/admin/liste', { headers:{'X-Admin-Password':mdp()} });
  if(!r.ok){ alert('Mot de passe incorrect'); return; }
  const data = await r.json();
  const tbody = document.querySelector('#tableLicences tbody');
  tbody.innerHTML = '';
  for(const l of data){
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${l.idMachine}</td><td>${l.entreprise||''}</td><td>${l.edition}</td>
      <td><code>${l.cle}</code></td><td>${l.dateExpiration||'Illimitee'}</td>
      <td>${l.revoquee?'<span class="badge badge-revoke">Revoquee</span>':'<span class="badge badge-ok">Active</span>'}</td>
      <td>${l.revoquee?'':'<button class="btn-revoke" onclick="revoquer('+l.id+')">Revoquer</button>'}</td>`;
    tbody.appendChild(tr);
  }
}

async function revoquer(id){
  if(!confirm('Revoquer cette licence ?')) return;
  await fetch('/admin/revoquer/'+id, { method:'POST', headers:{'X-Admin-Password':mdp()} });
  chargerListe();
}
</script>
</body>
</html>
""";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string DbPath() => Path.Combine(AppContext.BaseDirectory, "licences.sqlite");

// ── Initialisation base de donnees ──────────────────────────────────────────
void InitDb()
{
    using var c = new SqliteConnection($"Data Source={DbPath()}");
    c.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Licences(
            IdLicence INTEGER PRIMARY KEY AUTOINCREMENT,
            IdMachine TEXT NOT NULL,
            Cle TEXT NOT NULL UNIQUE,
            NomEntreprise TEXT DEFAULT '',
            Edition TEXT DEFAULT 'Standard',
            DateCreation TEXT NOT NULL,
            DateExpiration TEXT,
            DateActivation TEXT,
            Revoquee INTEGER DEFAULT 0,
            UNIQUE(IdMachine, Edition)
        );";
    cmd.ExecuteNonQuery();
}
InitDb();

string CalculerCle(string idMachine, string edition)
{
    string donnees = idMachine.ToUpper() + "|" + edition.ToUpper() + "|" + CLE_SECRETE;
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(donnees));
    var hex = Convert.ToHexString(hash).Substring(0, 20);
    return string.Join("-", new[] { hex[..5], hex[5..10], hex[10..15], hex[15..20] });
}

bool VerifierAdmin(HttpRequest req)
{
    var mdp = req.Headers["X-Admin-Password"].ToString();
    return mdp == MOT_DE_PASSE_ADMIN;
}

// ── API CLIENT: activation d'une licence (appelee depuis Tunisia ERP) ──────
app.MapPost("/api/activer", (ActivationRequest req) =>
{
    using var c = new SqliteConnection($"Data Source={DbPath()}");
    c.Open();

    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT * FROM Licences WHERE IdMachine=@im AND Edition=@ed";
    cmd.Parameters.AddWithValue("@im", req.IdMachine.ToUpper());
    cmd.Parameters.AddWithValue("@ed", req.Edition);
    using var r = cmd.ExecuteReader();

    if (!r.Read())
        return Results.Json(new ActivationResponse(false, "Aucune licence trouvee pour cette machine. Contactez le vendeur.", null, null));

    bool revoquee = Convert.ToInt32(r["Revoquee"]) == 1;
    if (revoquee)
        return Results.Json(new ActivationResponse(false, "Cette licence a ete revoquee. Contactez le vendeur.", null, null));

    string cleAttendue = r["Cle"]?.ToString() ?? "";
    if (!string.Equals(cleAttendue, req.Cle.Trim(), StringComparison.OrdinalIgnoreCase))
        return Results.Json(new ActivationResponse(false, "Cle de licence incorrecte.", null, null));

    string? dateExpStr = r["DateExpiration"]?.ToString();
    DateTime? dateExp = string.IsNullOrEmpty(dateExpStr) ? null : DateTime.Parse(dateExpStr);
    if (dateExp.HasValue && dateExp.Value.Date < DateTime.UtcNow.Date)
        return Results.Json(new ActivationResponse(false, "Cette licence a expire le " + dateExp.Value.ToString("dd/MM/yyyy") + ".", null, null));

    r.Close();

    // Marquer comme activee (date de premiere activation, si pas deja fait)
    using var upd = c.CreateCommand();
    upd.CommandText = "UPDATE Licences SET DateActivation=COALESCE(DateActivation,@da) WHERE IdMachine=@im AND Edition=@ed";
    upd.Parameters.AddWithValue("@da", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    upd.Parameters.AddWithValue("@im", req.IdMachine.ToUpper());
    upd.Parameters.AddWithValue("@ed", req.Edition);
    upd.ExecuteNonQuery();

    string nomEntreprise = "";
    using (var cn = c.CreateCommand())
    {
        cn.CommandText = "SELECT NomEntreprise FROM Licences WHERE IdMachine=@im AND Edition=@ed";
        cn.Parameters.AddWithValue("@im", req.IdMachine.ToUpper());
        cn.Parameters.AddWithValue("@ed", req.Edition);
        nomEntreprise = cn.ExecuteScalar()?.ToString() ?? "";
    }

    return Results.Json(new ActivationResponse(true, "Activation reussie.", dateExp?.ToString("yyyy-MM-dd"), nomEntreprise));
});

// ── ADMIN: creer une nouvelle licence (vous, le vendeur) ───────────────────
app.MapPost("/admin/creer", (HttpRequest http, CreerLicenceRequest req) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    string cle = CalculerCle(req.IdMachine, req.Edition);
    using var c = new SqliteConnection($"Data Source={DbPath()}");
    c.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = @"INSERT INTO Licences(IdMachine,Cle,NomEntreprise,Edition,DateCreation,DateExpiration)
                         VALUES(@im,@cl,@ne,@ed,@dc,@de)
                         ON CONFLICT(IdMachine,Edition) DO UPDATE SET Cle=@cl,NomEntreprise=@ne,DateExpiration=@de,Revoquee=0";
    cmd.Parameters.AddWithValue("@im", req.IdMachine.ToUpper());
    cmd.Parameters.AddWithValue("@cl", cle);
    cmd.Parameters.AddWithValue("@ne", req.NomEntreprise);
    cmd.Parameters.AddWithValue("@ed", req.Edition);
    cmd.Parameters.AddWithValue("@dc", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    cmd.Parameters.AddWithValue("@de", req.DateExpiration ?? (object)DBNull.Value);
    cmd.ExecuteNonQuery();

    return Results.Json(new { cle, message = "Licence creee. Envoyez cette cle au client." });
});

// ── ADMIN: lister toutes les licences ───────────────────────────────────────
app.MapGet("/admin/liste", (HttpRequest http) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    using var c = new SqliteConnection($"Data Source={DbPath()}");
    c.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT * FROM Licences ORDER BY DateCreation DESC";
    using var r = cmd.ExecuteReader();
    var liste = new List<object>();
    while (r.Read())
    {
        liste.Add(new
        {
            id = Convert.ToInt32(r["IdLicence"]),
            idMachine = r["IdMachine"]?.ToString(),
            cle = r["Cle"]?.ToString(),
            entreprise = r["NomEntreprise"]?.ToString(),
            edition = r["Edition"]?.ToString(),
            dateCreation = r["DateCreation"]?.ToString(),
            dateExpiration = r["DateExpiration"]?.ToString(),
            dateActivation = r["DateActivation"]?.ToString(),
            revoquee = Convert.ToInt32(r["Revoquee"]) == 1
        });
    }
    return Results.Json(liste);
});

// Programma aggiunto  :


// ── Modeles ──────────────────────────────────────────────────────────────
record ActivationRequest(string IdMachine, string Cle, string Edition);
record ActivationResponse(bool Succes, string Message, string? DateExpiration, string? NomEntreprise);
record CreerLicenceRequest(string IdMachine, string NomEntreprise, string Edition, string? DateExpiration);
//+++++++++++++++++++++++++++
// ═══════════════════════════════════════════════════════════════════════
// BACKUP ENDPOINT — da aggiungere in Program.cs (ServeurActivation)
// ═══════════════════════════════════════════════════════════════════════
// Aggiunge questi 3 endpoint al server esistente, prima di app.Run():
//
//   POST /backup/upload     → riceve il file .sqlite
//   GET  /backup/download   → scarica l'ultimo backup
//   GET  /backup/info       → info sull'ultimo backup (data, dimensione)
// ═══════════════════════════════════════════════════════════════════════

// ── Cartella dove salvare i backup sul server ─────────────────────────
string BackupDir => Path.Combine(AppContext.BaseDirectory, "backups");

void InitBackupDir()
{
    Directory.CreateDirectory(BackupDir);
}
InitBackupDir();

// ── POST /backup/upload ───────────────────────────────────────────────
// Riceve il file .sqlite e lo salva sul server
// Header richiesto: X-Admin-Password (stessa password admin)
// Body: file binario .sqlite (multipart/form-data o application/octet-stream)
app.MapPost("/backup/upload", async (HttpRequest http) =>
{
    if (!VerifierAdmin(http))
        return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    try
    {
        // Salva il backup con timestamp
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        string nomFichier = $"tndb_{timestamp}.sqlite";
        string cheminDest = Path.Combine(BackupDir, nomFichier);
        string cheminLast = Path.Combine(BackupDir, "tndb_latest.sqlite");

        // Legge il body (file binario)
        using var ms = new MemoryStream();
        await http.Body.CopyToAsync(ms);
        byte[] data = ms.ToArray();

        if (data.Length == 0)
            return Results.Json(new { erreur = "Fichier vide reçu" }, statusCode: 400);

        // Salva il backup con timestamp (storico)
        await File.WriteAllBytesAsync(cheminDest, data);

        // Aggiorna anche il "latest" (sovrascrive)
        await File.WriteAllBytesAsync(cheminLast, data);

        // Mantieni solo gli ultimi 7 backup (pulizia automatica)
        var backups = Directory.GetFiles(BackupDir, "tndb_2*.sqlite")
            .OrderByDescending(f => f).ToList();
        foreach (var old in backups.Skip(7))
            try { File.Delete(old); } catch { }

        return Results.Json(new
        {
            message = "Backup reçu et sauvegardé avec succès.",
            fichier = nomFichier,
            taille = data.Length,
            date = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss") + " UTC"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { erreur = "Erreur serveur: " + ex.Message }, statusCode: 500);
    }
});

// ── GET /backup/download ──────────────────────────────────────────────
// Télécharge le dernier backup
// Header requis: X-Admin-Password
app.MapGet("/backup/download", (HttpRequest http) =>
{
    if (!VerifierAdmin(http))
        return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    string cheminLast = Path.Combine(BackupDir, "tndb_latest.sqlite");
    if (!File.Exists(cheminLast))
        return Results.Json(new { erreur = "Aucun backup disponible sur le serveur." }, statusCode: 404);

    var bytes = File.ReadAllBytes(cheminLast);
    return Results.File(bytes, "application/octet-stream", "tndb_latest.sqlite");
});

// ── GET /backup/info ──────────────────────────────────────────────────
// Infos sur le dernier backup (date, taille, nb backups)
// Header requis: X-Admin-Password
app.MapGet("/backup/info", (HttpRequest http) =>
{
    if (!VerifierAdmin(http))
        return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    string cheminLast = Path.Combine(BackupDir, "tndb_latest.sqlite");
    if (!File.Exists(cheminLast))
        return Results.Json(new { disponible = false, message = "Aucun backup sur le serveur." });

    var info = new FileInfo(cheminLast);
    var backups = Directory.GetFiles(BackupDir, "tndb_2*.sqlite").Length;

    return Results.Json(new
    {
        disponible = true,
        dateModification = info.LastWriteTimeUtc.ToString("dd/MM/yyyy HH:mm:ss") + " UTC",
        tailleMo = Math.Round(info.Length / 1024.0 / 1024.0, 2),
        tailleOctets = info.Length,
        nbBackupsStockes = backups
    });
});

// programma aggiunto finisce qua 



// ── ADMIN: revoquer une licence ─────────────────────────────────────────────
app.MapPost("/admin/revoquer/{id}", (HttpRequest http, int id) =>
{
    if (!VerifierAdmin(http)) return Results.Json(new { erreur = "Mot de passe admin incorrect" }, statusCode: 401);

    using var c = new SqliteConnection($"Data Source={DbPath()}");
    c.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "UPDATE Licences SET Revoquee=1 WHERE IdLicence=@id";
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
    return Results.Json(new { message = "Licence revoquee." });
});

// ── ADMIN: page web simple pour gerer les licences sans coder ──────────────
app.MapGet("/admin", () => Results.Content(PageAdminHtml, "text/html; charset=utf-8"));

app.MapGet("/", () => "Serveur d'activation Tunisia ERP - operationnel.");

app.Run();
  



