# Serveur d'activation Tunisia ERP — Guide de deploiement

Ce petit service web gere les licences de Tunisia ERP : generation de cles,
verification a l'activation, et revocation. Il tourne en continu sur un
hebergement **gratuit** (Render.com), aucune carte bancaire requise.

---

## 1. Avant de deployer — changez ces 2 valeurs

Ouvrez `Program.cs` dans ce dossier et changez en haut du fichier :

```csharp
const string CLE_SECRETE = "TunisiaERP-2026-ClePriveeVendeur-ChangezMoi";
const string MOT_DE_PASSE_ADMIN = "ChangezMoi123!";
```

- `CLE_SECRETE` : une longue chaine aleatoire, secrete (jamais partagee).
- `MOT_DE_PASSE_ADMIN` : le mot de passe que VOUS utiliserez pour acceder
  a la page `/admin` et generer des licences. Choisissez quelque chose de fort.

---

## 2. Deploiement sur Render.com (gratuit, ~10 minutes)

### Etape A — Mettre le code sur GitHub

1. Creez un compte gratuit sur [github.com](https://github.com) si vous n'en avez pas.
2. Creez un nouveau depot (repository), par exemple `tunisiaerp-activation`.
3. Mettez-y **uniquement le contenu de ce dossier** `ServeurActivation/`
   (pas tout le projet Tunisia ERP, juste ce sous-dossier).

   Le plus simple : sur la page de votre nouveau depot GitHub, cliquez
   "Add file > Upload files", et glissez les fichiers `Program.cs`,
   `ServeurActivation.csproj`, et ce `README.md`.

### Etape B — Creer le service sur Render

1. Allez sur [render.com](https://render.com) et inscrivez-vous gratuitement
   (vous pouvez vous connecter directement avec votre compte GitHub).
2. Cliquez **New +** → **Web Service**.
3. Connectez votre depot GitHub `tunisiaerp-activation`.
4. Render detecte automatiquement un projet .NET. Verifiez/completez :
   - **Name** : `tunisiaerp-activation` (ou ce que vous voulez)
   - **Region** : choisissez la plus proche (Frankfurt par exemple)
   - **Branch** : `main`
   - **Runtime** : `Docker` n'est pas necessaire, choisissez **.NET** si propose,
     sinon laissez Render auto-detecter (il gere les projets .NET nativement)
   - **Build Command** : `dotnet publish -c Release -o out`
   - **Start Command** : `dotnet out/ServeurActivation.dll`
   - **Instance Type** : **Free**
5. Cliquez **Create Web Service**.

Render va construire et lancer votre service. Cela prend 2-5 minutes la
premiere fois. Une fois pret, Render vous donne une URL du type :

```
https://tunisiaerp-activation.onrender.com
```

**C'est l'URL de votre serveur.** Notez-la.

⚠️ **Particularite du plan gratuit Render** : le service s'endort apres
15 minutes d'inactivite, et met 30-50 secondes a se reveiller au prochain
appel. C'est sans consequence pour vous : l'activation n'est faite qu'une
fois par client, un delai de 30 secondes au pire est acceptable. L'application
Tunisia ERP elle-meme ne contacte JAMAIS le serveur apres l'activation
initiale (fonctionnement hors-ligne ensuite).

---

## 3. Configurer Tunisia ERP avec votre URL de serveur

Ouvrez `TunisiaERP/UI/LicenceHelper.cs` et remplacez :

```csharp
public static string UrlServeur = "https://CHANGEZ-MOI.onrender.com";
```

par votre vraie URL Render, par exemple :

```csharp
public static string UrlServeur = "https://tunisiaerp-activation.onrender.com";
```

Recompilez Tunisia ERP. C'est fait — l'application contactera desormais
votre serveur pour chaque activation.

---

## 4. Utilisation au quotidien (generer une licence pour un client)

1. Le client vous envoie son **ID Machine** (affiche dans l'ecran
   d'activation de Tunisia ERP, avec un bouton "Copier").
2. Allez sur `https://VOTRE-URL.onrender.com/admin`
3. Saisissez votre mot de passe admin en haut.
4. Dans "Creer une nouvelle licence" :
   - Collez l'ID Machine du client
   - Saisissez le nom de son entreprise
   - Choisissez l'edition (Standard / Pro / Entreprise)
   - Optionnel : date d'expiration (sinon, l'application appliquera 1 an
     par defaut)
5. Cliquez **Generer la cle**. La cle s'affiche immediatement.
6. Envoyez cette cle au client par email/WhatsApp.
7. Le client la saisit dans Tunisia ERP → activation immediate (si le
   serveur Render est "reveille" ; sinon il patiente ~30s automatiquement).

La page `/admin` liste aussi toutes les licences existantes, avec un
bouton **Revoquer** pour bloquer instantanement un client (par exemple
en cas d'impaye). La revocation prend effet immediatement si le client
retente une activation, mais **n'affecte pas** une installation deja
activee avec succes (elle continue de fonctionner hors-ligne jusqu'a
expiration — pour une coupure immediate, il faudrait une verification
periodique, plus complexe).

---

## 5. Tester localement avant de deployer (optionnel)

Si vous avez le SDK .NET installe sur votre PC :

```
cd ServeurActivation
dotnet run
```

Le serveur demarre sur `http://localhost:5000` (ou un port affiche dans
la console). Vous pouvez ouvrir `http://localhost:5000/admin` pour tester
l'interface avant de deployer sur Render.
