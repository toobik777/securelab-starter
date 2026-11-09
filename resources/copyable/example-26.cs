policy.WithOrigins("http://localhost:4173")
      .WithMethods("GET")
      .WithHeaders("X-Lab-Cors-Probe")
      .AllowCredentials();
