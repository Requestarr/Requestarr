using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using NzbDrone.Core.Authentication;
using Requestarr.Core.Audit;

namespace Requestarr.Http
{
    public static class WebAdmin
    {
        // Who acts in the web app. Its pages call the API with the API key, so the request's own user is the key; the
        // admin who signed in is in the sign-in cookie, where Servarr keeps their name as the "user" claim. No cookie
        // (sign-in off for local addresses, or a script with the key): an admin without a name.
        public static async Task<Actor> ActorAsync(HttpContext context)
        {
            var signIn = await context.AuthenticateAsync(nameof(AuthenticationType.Forms));
            return Actor.Web(signIn.Principal?.FindFirst("user")?.Value);
        }
    }
}
