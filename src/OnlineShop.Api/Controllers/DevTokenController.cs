using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/dev")]
public sealed class DevTokenController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;

    public DevTokenController(IConfiguration config, IHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public sealed record DevTokenRequest(string Email, string Role = "MasterAdmin");

    [HttpPost("token")]
    [AllowAnonymous]
    public IActionResult CreateToken([FromBody] DevTokenRequest req)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var key = _config["Jwt:Key"];
        var issuer = _config["Jwt:Issuer"] ?? "OnlineShop";
        var audience = _config["Jwt:Audience"] ?? "OnlineShop";

        if (string.IsNullOrWhiteSpace(key))
            return Problem("Falta Jwt:Key en configuración (appsettings.Development.json recomendado).");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, req.Email),
            new Claim(JwtRegisteredClaimNames.Email, req.Email),
            new Claim(ClaimTypes.Role, req.Role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { token = jwt, expiresUtc = token.ValidTo });
    }
}