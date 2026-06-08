import os
from fastapi import FastAPI, Query
from fastapi.responses import HTMLResponse
from typing import List
from contextlib import asynccontextmanager

from scalar_fastapi import get_scalar_api_reference, Layout, Theme

from models import User, UserCreate
from database import DatabaseManager, UserRepository
from scalar_theme import SCALAR_CUSTOM_CSS, SCALAR_FAVICON_URL, SCALAR_TITLE


def _is_truthy(value):
    return str(value or "").strip().lower() in {"1", "true", "yes", "on"}


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup: create the users table if it doesn't exist.
    #
    # SKIP_DB_INIT is an opt-in escape hatch (default off) that lets the static
    # Scalar API reference render without a database, e.g. when capturing docs
    # screenshots. Normal runs keep requiring PostgreSQL.
    if not _is_truthy(os.getenv("SKIP_DB_INIT")):
        await DatabaseManager.initialize_database()
    yield
    # Shutdown: cleanup if needed


app = FastAPI(
    title="User API",
    description=(
        "A FastAPI service backed by **PostgreSQL**, orchestrated with **Aspire**.\n\n"
        "Explore and try every operation right here in the interactive **Scalar** "
        "API reference. The classic Swagger UI is also available at `/docs`."
    ),
    version="1.0.0",
    lifespan=lifespan,
)


@app.get("/", tags=["System"])
def read_root():
    return {
        "message": "User API",
        "endpoints": ["/users", "/users/{id}", "/health"],
        "documentation": {"scalar": "/scalar", "swagger": "/docs", "redoc": "/redoc"},
    }


@app.get("/scalar", include_in_schema=False)
def scalar_reference():
    """Serve the branded Scalar API reference."""
    reference = get_scalar_api_reference(
        openapi_url=app.openapi_url,
        title=SCALAR_TITLE,
        theme=Theme.NONE,
        custom_css=SCALAR_CUSTOM_CSS,
        scalar_favicon_url=SCALAR_FAVICON_URL,
        dark_mode=True,
        layout=Layout.MODERN,
        default_open_all_tags=True,
    )
    # scalar-fastapi emits a bare <html> tag; add a language for accessibility.
    html = reference.body.decode("utf-8").replace("<html>", '<html lang="en">', 1)
    return HTMLResponse(html)


@app.get("/health", tags=["System"])
async def health_check():
    return await DatabaseManager.check_health()


@app.get("/users", response_model=List[User], tags=["Users"])
async def get_users(
    limit: int = Query(default=100, ge=1, le=100),
    offset: int = Query(default=0, ge=0),
):
    return await UserRepository.get_all(limit=limit, offset=offset)


@app.get("/users/{user_id}", response_model=User, tags=["Users"])
async def get_user(user_id: int):
    return await UserRepository.get_by_id(user_id)


@app.post("/users", response_model=User, tags=["Users"])
async def create_user(user: UserCreate):
    return await UserRepository.create(user)


@app.delete("/users/{user_id}", tags=["Users"])
async def delete_user(user_id: int):
    return await UserRepository.delete(user_id)
