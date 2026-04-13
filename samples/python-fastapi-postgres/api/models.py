from pydantic import BaseModel
from typing import Optional


class User(BaseModel):
    id: Optional[int] = None
    name: str
    email: str


class UserCreate(BaseModel):
    name: str
    email: str